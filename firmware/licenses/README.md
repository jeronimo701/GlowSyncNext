# Firmware dependencies

The accompanying sketch is MIT licensed (root LICENSE). FastLED 3.9.15 is MIT; its license is copied into binary packages as FastLED-MIT.txt.

Arduino AVR core, supplied by PlatformIO framework-arduino-avr 5.2.0, is licensed under LGPL 2.1 or later. Source: https://github.com/arduino/ArduinoCore-avr .

Arduino ESP32 core 2.0.17 is licensed under LGPL 2.1 or later. Source: https://github.com/espressif/arduino-esp32/tree/2.0.17 . The framework includes ESP-IDF libraries with Apache-2.0 and component-specific licenses: https://github.com/espressif/esp-idf/tree/v4.4.7 .

The full LGPL 2.1 text is included here. Upstream source and license notices are retained in the packages installed by PlatformIO; platformio.ini pins the platform/library versions to reproduce the examples. Binary bundles include the application source and configuration, and do not include compiler binaries. The sketch and settings can be rebuilt and relinked using those sources and PlatformIO.
