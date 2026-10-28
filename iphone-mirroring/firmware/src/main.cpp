// iPhone kontrolcüsü: iPhone'a Bluetooth LE fare + klavye + medya tuşları
// olarak görünür ve Windows uygulamasından gelen satır komutlarını HID
// raporlarına çevirir.
//
// Seri protokol (115200 baud, her komut bir satır):
//   ?                   durum sorgusu -> "IPM bagli" / "IPM bekliyor"
//   r <dx> <dy> <n>     aynı fare hareketini n kez gönder (|dx|,|dy| <= 127)
//   m <dx> <dy>         toplam hareket; 127'lik parçalara bölünür
//   b <maske>           fare tuşları (1 sol, 2 sağ, 4 orta)
//   w <v>               dikey kaydırma (+ yukarı)
//   k <mod> [k1..k6]    klavye raporu (HID kullanım kodları)
//   c <kullanım>        medya/sistem tuşu bas-bırak (Consumer page)
//   i <ms>              rapor aralığı (varsayılan 8 ms)

#include <Arduino.h>
#include <NimBLEDevice.h>
#include <NimBLEHIDDevice.h>

static const uint8_t REPORT_MAP[] = {
    // Klavye (rapor 1): değiştiriciler, boş bayt, 6 tuş
    0x05, 0x01, 0x09, 0x06, 0xA1, 0x01, 0x85, 0x01,
    0x05, 0x07, 0x19, 0xE0, 0x29, 0xE7, 0x15, 0x00, 0x25, 0x01,
    0x75, 0x01, 0x95, 0x08, 0x81, 0x02,
    0x95, 0x01, 0x75, 0x08, 0x81, 0x01,
    0x95, 0x06, 0x75, 0x08, 0x15, 0x00, 0x25, 0x65,
    0x05, 0x07, 0x19, 0x00, 0x29, 0x65, 0x81, 0x00,
    0xC0,
    // Fare (rapor 2): 5 tuş, X, Y, tekerlek, yatay kaydırma
    0x05, 0x01, 0x09, 0x02, 0xA1, 0x01, 0x85, 0x02, 0x09, 0x01, 0xA1, 0x00,
    0x05, 0x09, 0x19, 0x01, 0x29, 0x05, 0x15, 0x00, 0x25, 0x01,
    0x95, 0x05, 0x75, 0x01, 0x81, 0x02,
    0x95, 0x01, 0x75, 0x03, 0x81, 0x01,
    0x05, 0x01, 0x09, 0x30, 0x09, 0x31, 0x09, 0x38,
    0x15, 0x81, 0x25, 0x7F, 0x75, 0x08, 0x95, 0x03, 0x81, 0x06,
    0x05, 0x0C, 0x0A, 0x38, 0x02,
    0x15, 0x81, 0x25, 0x7F, 0x75, 0x08, 0x95, 0x01, 0x81, 0x06,
    0xC0, 0xC0,
    // Consumer (rapor 3): tek 16 bit kullanım kodu
    0x05, 0x0C, 0x09, 0x01, 0xA1, 0x01, 0x85, 0x03,
    0x15, 0x00, 0x26, 0xFF, 0x03, 0x19, 0x00, 0x2A, 0xFF, 0x03,
    0x75, 0x10, 0x95, 0x01, 0x81, 0x00,
    0xC0,
};

static NimBLEServer *server;
static NimBLEHIDDevice *hid;
static NimBLECharacteristic *keyboardInput;
static NimBLECharacteristic *mouseInput;
static NimBLECharacteristic *consumerInput;

static uint8_t buttons = 0;
static uint32_t intervalMs = 8;
static bool wasConnected = false;

static bool connected() { return server->getConnectedCount() > 0; }

class ServerCallbacks : public NimBLEServerCallbacks {
    void onConnect(NimBLEServer *s, ble_gap_conn_desc *desc) override {
        // 7.5-15 ms bağlantı aralığı: imleç gecikmesini düşük tutar.
        s->updateConnParams(desc->conn_handle, 6, 12, 0, 200);
    }
};

static void sendMouse(int8_t dx, int8_t dy, int8_t wheel) {
    uint8_t report[5] = {buttons, (uint8_t)dx, (uint8_t)dy, (uint8_t)wheel, 0};
    mouseInput->setValue(report, sizeof(report));
    mouseInput->notify();
    delay(intervalMs);
}

static int8_t clamp127(long v) { return (int8_t)constrain(v, -127L, 127L); }

static void moveTotal(long dx, long dy) {
    while (dx != 0 || dy != 0) {
        int8_t sx = clamp127(dx), sy = clamp127(dy);
        sendMouse(sx, sy, 0);
        dx -= sx;
        dy -= sy;
    }
}

static void handleLine(char *line) {
    long a = 0, b = 0, c = 0;
    switch (line[0]) {
    case '?':
        Serial.println(connected() ? "IPM bagli" : "IPM bekliyor");
        return;
    case 'i':
        if (sscanf(line + 1, "%ld", &a) == 1) intervalMs = constrain(a, 1L, 100L);
        return;
    }
    if (!connected()) return;

    switch (line[0]) {
    case 'r':
        if (sscanf(line + 1, "%ld %ld %ld", &a, &b, &c) == 3)
            for (long n = 0; n < c && n < 2000; n++) sendMouse(clamp127(a), clamp127(b), 0);
        break;
    case 'm':
        if (sscanf(line + 1, "%ld %ld", &a, &b) == 2) moveTotal(a, b);
        break;
    case 'b':
        if (sscanf(line + 1, "%ld", &a) == 1) {
            buttons = (uint8_t)(a & 0x1F);
            sendMouse(0, 0, 0);
        }
        break;
    case 'w':
        if (sscanf(line + 1, "%ld", &a) == 1) sendMouse(0, 0, clamp127(a));
        break;
    case 'k': {
        long v[7] = {0};
        sscanf(line + 1, "%ld %ld %ld %ld %ld %ld %ld", &v[0], &v[1], &v[2], &v[3], &v[4], &v[5], &v[6]);
        uint8_t report[8] = {(uint8_t)v[0], 0, (uint8_t)v[1], (uint8_t)v[2],
                             (uint8_t)v[3], (uint8_t)v[4], (uint8_t)v[5], (uint8_t)v[6]};
        keyboardInput->setValue(report, sizeof(report));
        keyboardInput->notify();
        delay(intervalMs);
        break;
    }
    case 'c':
        if (sscanf(line + 1, "%ld", &a) == 1) {
            uint8_t down[2] = {(uint8_t)(a & 0xFF), (uint8_t)((a >> 8) & 0xFF)};
            uint8_t up[2] = {0, 0};
            consumerInput->setValue(down, 2);
            consumerInput->notify();
            delay(60);
            consumerInput->setValue(up, 2);
            consumerInput->notify();
            delay(intervalMs);
        }
        break;
    }
}

void setup() {
    Serial.setRxBufferSize(4096);
    Serial.begin(115200);

    NimBLEDevice::init("iPhone Kontrol (Sureklilik)");
    NimBLEDevice::setSecurityAuth(true, false, true);
    NimBLEDevice::setSecurityIOCap(BLE_HS_IO_NO_INPUT_OUTPUT);

    server = NimBLEDevice::createServer();
    server->setCallbacks(new ServerCallbacks());

    hid = new NimBLEHIDDevice(server);
    keyboardInput = hid->inputReport(1);
    mouseInput = hid->inputReport(2);
    consumerInput = hid->inputReport(3);
    hid->manufacturer()->setValue("Sureklilik");
    hid->pnp(0x02, 0xe502, 0xa111, 0x0100);
    hid->hidInfo(0x00, 0x02);
    hid->reportMap((uint8_t *)REPORT_MAP, sizeof(REPORT_MAP));
    hid->startServices();
    hid->setBatteryLevel(100);

    NimBLEAdvertising *adv = server->getAdvertising();
    adv->setAppearance(0x03C0);  // genel HID cihazı
    adv->addServiceUUID(hid->hidService()->getUUID());
    adv->setScanResponse(true);
    adv->start();

    Serial.println("IPM hazir");
}

void loop() {
    static char line[96];
    static size_t len = 0;

    bool now = connected();
    if (now != wasConnected) {
        wasConnected = now;
        if (!now) buttons = 0;
        Serial.println(now ? "IPM bagli" : "IPM bekliyor");
    }

    while (Serial.available()) {
        char ch = (char)Serial.read();
        if (ch == '\r') continue;
        if (ch == '\n') {
            line[len] = 0;
            if (len > 0) handleLine(line);
            len = 0;
        } else if (len < sizeof(line) - 1) {
            line[len++] = ch;
        }
    }
    delay(1);
}
