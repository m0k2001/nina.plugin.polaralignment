#include <Arduino.h>
#include <ESPmDNS.h>
#include <Preferences.h>
#include <WiFi.h>

// ---------------------------------------------------------------------------
// Configuration WiFi — identifiants dans wifi_credentials.h (gitignore)
// Copier wifi_credentials.example.h -> wifi_credentials.h et renseigner
// ---------------------------------------------------------------------------
#include "wifi_credentials.h"
#define TCP_PORT      23

// ---------------------------------------------------------------------------
// Limites de position (unites internes)
// ---------------------------------------------------------------------------
const float X_MIN = -50.0f, X_MAX = 50.0f;
const float Y_MIN = -50.0f, Y_MAX = 50.0f;
const float Z_MIN = -10.0f, Z_MAX = 10.0f;

const float SIM_SPEED_FACTOR = 2.0f;

// ---------------------------------------------------------------------------
// Variables globales
// ---------------------------------------------------------------------------
float xPos = 0.0f, yPos = 0.0f, zPos = 0.0f;
float xTarget = 0.0f, yTarget = 0.0f;
float xSpeed = 0.0f, ySpeed = 0.0f;
unsigned long lastUpdateMicros = 0;
bool moving = false;

// Parametres moteur AAPA
int xRunCurrent = 800;   // mA
int yRunCurrent = 800;
int xHoldPercent = 50;    // %
int yHoldPercent = 50;

String serialBuffer = "";

Preferences prefs;
WiFiServer tcpServer(TCP_PORT);
WiFiClient tcpClient;
String tcpBuffer = "";

// ---------------------------------------------------------------------------
// NVS
// ---------------------------------------------------------------------------
void savePosition() {
    prefs.begin("aapa", false);
    prefs.putFloat("xPos", xPos);
    prefs.putFloat("yPos", yPos);
    prefs.putFloat("zPos", zPos);
    prefs.putBool("valid", true);
    prefs.end();
}

void loadPosition() {
    prefs.begin("aapa", true);
    bool valid = prefs.getBool("valid", false);
    if (!valid) {
        prefs.end();
        xPos = yPos = zPos = 0.0f;
        savePosition();
        return;
    }
    xPos = prefs.getFloat("xPos", 0.0f);
    yPos = prefs.getFloat("yPos", 0.0f);
    zPos = prefs.getFloat("zPos", 0.0f);
    prefs.end();

    if (isnan(xPos) || xPos < X_MIN || xPos > X_MAX) xPos = 0.0f;
    if (isnan(yPos) || yPos < Y_MIN || yPos > Y_MAX) yPos = 0.0f;
    if (isnan(zPos) || zPos < Z_MIN || zPos > Z_MAX) zPos = 0.0f;
}

// ---------------------------------------------------------------------------
// Messages — protocole AAPA : terminateur \n uniquement
// ---------------------------------------------------------------------------
String buildStatus() {
    // Format AAPA etendu : <status|MPos:x,y,z|T:target,R:running,E:endstop,S:speed|>
    String resp = "<";
    resp += (moving ? "Run" : "Idle");
    resp += "|MPos:";
    resp += (xPos >= 0.0f ? "+" : ""); resp += String(xPos, 3); resp += ",";
    resp += (yPos >= 0.0f ? "+" : ""); resp += String(yPos, 3); resp += ",";
    resp += (zPos >= 0.0f ? "+" : ""); resp += String(zPos, 3);
    resp += "|T:";
    resp += String((int)xTarget);
    resp += ",R:";
    resp += (moving ? "1" : "0");
    resp += ",E:0,S:";
    resp += String(xSpeed, 1);
    resp += "|>\nOK\n";
    return resp;
}

String buildAlarm(const String& msg) {
    return "<Alarm|Error:" + msg + "|>\n";
}

// ---------------------------------------------------------------------------
// Parsing
// ---------------------------------------------------------------------------
float extractValue(const String& cmd, int axisIndex) {
    int fIdx = cmd.indexOf('F', axisIndex);
    if (fIdx == -1) fIdx = cmd.length();
    return cmd.substring(axisIndex + 1, fIdx).toFloat();
}

float extractFeedrate(const String& cmd) {
    int fIdx = cmd.lastIndexOf('F');
    if (fIdx == -1) return 700.0f;
    return cmd.substring(fIdx + 1).toFloat();
}

// ---------------------------------------------------------------------------
// Commandes
// ---------------------------------------------------------------------------
void processMove(const String& cmd, bool relative, Stream& out) {
    float feedrate = extractFeedrate(cmd);
    float speed = feedrate / 700.0f * SIM_SPEED_FACTOR;

    float newX = xTarget, newY = yTarget;
    bool doMoveX = false, doMoveY = false;
    int idx;

    idx = cmd.indexOf('X');
    if (idx != -1) {
        float val = extractValue(cmd, idx);
        newX = relative ? xPos + val : val;
        if (newX < X_MIN || newX > X_MAX) { out.print(buildAlarm("Limit Exceeded")); out.print("OK\n"); return; }
        doMoveX = true;
    }

    idx = cmd.indexOf('Y');
    if (idx != -1) {
        float val = extractValue(cmd, idx);
        newY = relative ? yPos + val : val;
        if (newY < Y_MIN || newY > Y_MAX) { out.print(buildAlarm("Limit Exceeded")); out.print("OK\n"); return; }
        doMoveY = true;
    }

    idx = cmd.indexOf('Z');
    if (idx != -1) {
        float val = extractValue(cmd, idx);
        float newZ = relative ? zPos + val : val;
        if (newZ < Z_MIN || newZ > Z_MAX) { out.print(buildAlarm("Limit Exceeded")); out.print("OK\n"); return; }
        zPos = newZ;
    }

    if (doMoveX) { xTarget = newX; xSpeed = speed; }
    if (doMoveY) { yTarget = newY; ySpeed = speed; }
    if (doMoveX || doMoveY) { moving = true; lastUpdateMicros = micros(); }

    out.print("OK\n");
}

void processMotorCommand(const String& cmd, Stream& out) {
    int value = cmd.substring(2).toInt();

    if (cmd.startsWith("XC"))      xRunCurrent = value;
    else if (cmd.startsWith("YC")) yRunCurrent = value;
    else if (cmd.startsWith("XH")) xHoldPercent = value;
    else if (cmd.startsWith("YH")) yHoldPercent = value;

    out.print("OK\n");
}

void processCommand(const String& cmd, Stream& out) {
    if (cmd == "?") {
        out.print(buildStatus());
    } else if (cmd.startsWith("$J=G91G21")) {
        processMove(cmd, true, out);
    } else if (cmd.startsWith("$J=G53")) {
        processMove(cmd, false, out);
    } else if (cmd.startsWith("XC") || cmd.startsWith("YC") ||
               cmd.startsWith("XH") || cmd.startsWith("YH")) {
        processMotorCommand(cmd, out);
    } else {
        out.print(buildAlarm("Invalid Command"));
    }
}

// ---------------------------------------------------------------------------
// Simulation
// ---------------------------------------------------------------------------
void updateMotion() {
    if (!moving) return;
    unsigned long now = micros();
    float dt = (now - lastUpdateMicros) / 1000000.0f;
    lastUpdateMicros = now;
    bool doneX = true, doneY = true;

    if (abs(xPos - xTarget) > 0.001f) {
        float step = xSpeed * dt;
        xPos = (step >= abs(xTarget - xPos)) ? xTarget : xPos + ((xTarget > xPos) ? step : -step);
        doneX = false;
    }
    if (abs(yPos - yTarget) > 0.001f) {
        float step = ySpeed * dt;
        yPos = (step >= abs(yTarget - yPos)) ? yTarget : yPos + ((yTarget > yPos) ? step : -step);
        doneY = false;
    }
    if (doneX && doneY) { moving = false; savePosition(); }
}

// ---------------------------------------------------------------------------
// Serial
// ---------------------------------------------------------------------------
void handleSerial() {
    while (Serial.available()) {
        char c = Serial.read();
        if (c == '\n' || c == '\r') {
            if (serialBuffer.length() > 0) { processCommand(serialBuffer, Serial); serialBuffer = ""; }
        } else {
            serialBuffer += c;
            if (serialBuffer == "?") { processCommand(serialBuffer, Serial); serialBuffer = ""; }
        }
    }
}

// ---------------------------------------------------------------------------
// TCP
// ---------------------------------------------------------------------------
void handleTcp() {
    if (!tcpClient || !tcpClient.connected()) {
        tcpClient = tcpServer.accept();
        if (tcpClient) { tcpBuffer = ""; tcpClient.print(buildStatus()); }
        return;
    }
    while (tcpClient.available()) {
        char c = tcpClient.read();
        if (c == '\n' || c == '\r') {
            if (tcpBuffer.length() > 0) { processCommand(tcpBuffer, tcpClient); tcpBuffer = ""; }
        } else {
            tcpBuffer += c;
            if (tcpBuffer == "?") { processCommand(tcpBuffer, tcpClient); tcpBuffer = ""; }
        }
    }
}

// ---------------------------------------------------------------------------
// Setup
// ---------------------------------------------------------------------------
void setup() {
    Serial.begin(115200);
    unsigned long start = millis();
    while (!Serial && (millis() - start < 3000)) delay(10);

    loadPosition();
    xTarget = xPos;
    yTarget = yPos;
    serialBuffer.reserve(64);
    tcpBuffer.reserve(64);

    // Connexion WiFi en DHCP
    WiFi.mode(WIFI_STA);
    WiFi.setHostname("AAPA-ESP32");
    WiFi.begin(WIFI_SSID, WIFI_PASSWORD);
    Serial.printf("SSID: [%s]  (len=%d)\n", WIFI_SSID, strlen(WIFI_SSID));
    Serial.print("Connecting to WiFi");
    unsigned long wifiStart = millis();
    while (WiFi.status() != WL_CONNECTED && (millis() - wifiStart < 15000)) {
        delay(500);
        Serial.print(".");
    }

    if (WiFi.status() == WL_CONNECTED) {
        Serial.printf("\nWiFi OK - IP: %s  GW: %s\n",
                      WiFi.localIP().toString().c_str(),
                      WiFi.gatewayIP().toString().c_str());
        tcpServer.begin();
        Serial.printf("TCP server on port %d\n", TCP_PORT);

        if (MDNS.begin("AAPA-ESP32")) {
            MDNS.addService("aapa", "tcp", TCP_PORT);
            Serial.println("mDNS: AAPA-ESP32.local");
        } else {
            Serial.println("mDNS: FAILED");
        }
    } else {
        Serial.printf("\nWiFi FAILED (status=%d) - Serial only\n", WiFi.status());
        Serial.println("Check: SSID correct? 2.4GHz band? Password?");
    }

    Serial.print(buildStatus());
}

// ---------------------------------------------------------------------------
// Loop
// ---------------------------------------------------------------------------
void loop() {
    handleSerial();
    if (WiFi.status() == WL_CONNECTED) handleTcp();
    updateMotion();
}
