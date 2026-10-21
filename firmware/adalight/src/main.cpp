#include <Arduino.h>
#include <FastLED.h>

// Classic Adalight: Ada + (LED count - 1) big-endian + hi ^ lo ^ 0x55 + RGB.
// Fixed-size memory; incomplete/invalid frames never call show().
#ifndef LED_COUNT
#define LED_COUNT 81
#endif
#ifndef GS_DATA_PIN
#define GS_DATA_PIN 6
#endif
#ifndef SERIAL_BAUD
#define SERIAL_BAUD 115200
#endif
#ifndef MAX_MILLIAMPS
#define MAX_MILLIAMPS 1500
#endif
static_assert(LED_COUNT > 0 && LED_COUNT <= 1000, "LED_COUNT must be 1..1000");
CRGB leds[LED_COUNT];
uint8_t header[6];
uint8_t headerPos = 0;
uint32_t payloadPos = 0, expectedBytes = 0;
uint32_t lastByteAt = 0, lastFrameAt = 0;
bool validFrame = false, lit = false;

void resetParser() { headerPos = 0; payloadPos = expectedBytes = 0; validFrame = false; }

void acceptByte(uint8_t value) {
    lastByteAt = millis();
    if (expectedBytes) {
        if (validFrame) leds[payloadPos / 3].raw[payloadPos % 3] = value;
        if (++payloadPos == expectedBytes) {
            if (validFrame) { FastLED.show(); lastFrameAt = millis(); lit = true; }
            resetParser();
        }
        return;
    }
    if (headerPos < 3) {
        const char magic[] = "Ada";
        if (value == magic[headerPos]) header[headerPos++] = value;
        else headerPos = value == 'A' ? 1 : 0;
        return;
    }
    header[headerPos++] = value;
    if (headerPos == 6) {
        if ((header[3] ^ header[4] ^ 0x55) != header[5]) { resetParser(); return; }
        uint32_t count = (uint32_t(header[3]) << 8) + header[4] + 1;
        validFrame = count == LED_COUNT;
        expectedBytes = count * 3;
        payloadPos = 0;
    }
}

void setup() {
#if defined(ESP32)
    Serial.setRxBufferSize(2048);
#endif
    Serial.begin(SERIAL_BAUD);
    FastLED.addLeds<WS2812B, GS_DATA_PIN, GRB>(leds, LED_COUNT);
    FastLED.setMaxPowerInVoltsAndMilliamps(5, MAX_MILLIAMPS);
    FastLED.clear(true);
}

void loop() {
    if ((headerPos || expectedBytes) && uint32_t(millis() - lastByteAt) > 100) resetParser();
    while (Serial.available()) acceptByte(uint8_t(Serial.read()));
    if (lit && uint32_t(millis() - lastFrameAt) > 2000) { FastLED.clear(true); lit = false; }
#if defined(ESP32)
    delay(1);
#endif
}
