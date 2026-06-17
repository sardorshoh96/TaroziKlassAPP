# ADB orqali Device Owner o'rnatish

Bu qo'llanma ADB orqali TaroziAPP ilovasini Device Owner sifatida o'rnatish uchun.

## Talablar

1. **ADB o'rnatilgan bo'lishi kerak** (Android SDK Platform Tools)
2. **Qurilma USB orqali ulangan** yoki **Wi-Fi ADB** yoqilgan
3. **Qurilma factory reset qilingan** yoki **yangi qurilma** (Device Owner faqat bunday qurilmalarda o'rnatiladi)

## Qadamlar

### 1. ADB ulanishini tekshirish

```bash
adb devices
```

Qurilma ro'yxatda ko'rinishi kerak.

### 2. Ilovani o'rnatish

```bash
adb install -r uz.taroziklass-Signed.apk
```

Yoki Release papkasidan:
```bash
adb install -r bin/Release/net10.0-android/uz.taroziklass-Signed.apk
```

### 3. Device Owner o'rnatish

**Muhim:** Bu buyruq faqat factory reset qilingan yoki yangi qurilmalarda ishlaydi!

```bash
adb shell dpm set-device-owner uz.taroziklass/TaroziAPP.Platforms.Android.DeviceAdminReceiver
```

### 4. Tekshirish

Device Owner muvaffaqiyatli o'rnatilganini tekshirish:

```bash
adb shell dpm list-owners
```

Quyidagicha chiqishi kerak:
```
uz.taroziklass
```

## Xatoliklar va yechimlar

### Xato: "Not allowed to set the device owner because there are already some accounts on the device"

**Yechim:** Qurilmani factory reset qiling yoki yangi qurilma ishlating.

### Xato: "Not allowed to set the device owner because there are already some users on the device"

**Yechim:** Barcha qo'shimcha foydalanuvchilarni o'chiring.

### Xato: "Component not found"

**Yechim:** AndroidManifest.xml'da receiver nomi to'g'ri ekanligini tekshiring:
- AndroidManifest.xml: `android:name="TaroziAPP.Platforms.Android.DeviceAdminReceiver"`
- KioskService.cs: `Name = "TaroziAPP.Platforms.Android.DeviceAdminReceiver"`

Component name: `uz.taroziklass/TaroziAPP.Platforms.Android.DeviceAdminReceiver`

## Qo'shimcha buyruqlar

### Device Owner'ni o'chirish

```bash
adb shell dpm remove-active-admin uz.taroziklass/TaroziAPP.Platforms.Android.DeviceAdminReceiver
```

### Lock Task mode'ni tekshirish

```bash
adb shell dpm get-lock-task-packages
```

### Ilovani o'chirish

```bash
adb uninstall uz.taroziklass
```

## Eslatmalar

1. Device Owner o'rnatilgandan keyin, ilovani o'chirish qiyin bo'ladi
2. Factory reset qilish Device Owner'ni o'chiradi
3. Ilova Device Owner bo'lgandan keyin kiosk mode'ni faollashtirishi mumkin

