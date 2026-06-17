# Kiosk Mode'ni O'chirish

## Usul 1: Ilova ichida (Tavsiya etiladi)

1. Ilovani oching
2. **Settings** bo'limiga kiring
3. **"🏪 Kiosk rejimi"** switch'ini toping
4. Switch'ni **o'chiring** (false qiling)
5. Agar taklif qilinsa, ilovani qayta ishga tushiring

## Usul 2: ADB orqali (Agar ilova ichida o'chirib bo'lmasa)

### Lock Task Mode'ni o'chirish:

```bash
adb shell am task lock stop
```

### Lock Task Packages ro'yxatini tozalash:

```bash
adb shell dpm set-lock-task-packages --user 0
```

### Persistent Preferred Activity'ni o'chirish:

```bash
adb shell dpm clear-package-persistent-preferred-activities uz.taroziklass
```

## Usul 3: Device Owner'ni o'chirish (Oxirgi chora)

Agar boshqa usullar ishlamasa, Device Owner'ni o'chirish:

```bash
adb shell dpm remove-active-admin uz.taroziklass/TaroziAPP.Platforms.Android.DeviceAdminReceiver
```

**Eslatma:** Device Owner o'chirilgandan keyin, uni qayta o'rnatish uchun qurilmani factory reset qilish kerak bo'ladi.

## Tekshirish

Kiosk mode o'chirilganini tekshirish:

```bash
adb shell dpm get-lock-task-packages
```

Bo'sh ro'yxat chiqishi kerak.

## Muammo hal qilish

### Ilova pin ekranidan chiqmayapti:

1. ADB orqali lock task mode'ni o'chiring:
   ```bash
   adb shell am task lock stop
   ```

2. Ilovani qayta ishga tushiring:
   ```bash
   adb shell am force-stop uz.taroziklass
   adb shell am start -n uz.taroziklass/crc64b794ac46b6fa8a0b.MainActivity
   ```

### Switch ishlamayapti:

1. Ilovani qayta ishga tushiring
2. Settings bo'limiga qaytib kiring
3. Agar hali ham ishlamasa, ADB usullaridan foydalaning

