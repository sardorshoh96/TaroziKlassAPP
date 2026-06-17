# CppNative Build Instructions

## .so faylini build qilish (ARM64 faqat)

### Visual Studio'da build qilish:

1. **Visual Studio'ni oching**
2. **CppNative.vcxproj** faylini oching
3. **Platform'ni tanlang:** ARM64
4. **Configuration'ni tanlang:** Debug yoki Release
5. **Build → Build Solution** (Ctrl+Shift+B)

### Yaratilgan fayl:

Build qilingandan keyin quyidagi papkada `.so` fayl yaratiladi:

- `Platforms\Android\libs\arm64-v8a\libserialport.so`

### Eslatma:

- Faqat ARM64 (arm64-v8a) build qilish kerak
- `.so` fayl nomi `libserialport.so` bo'lishi kerak
- TaroziAPP.csproj avtomatik ravishda bu faylni topadi va APK'ga qo'shadi

### Build qilingandan keyin:

1. `.so` fayl to'g'ri joyda ekanligini tekshiring:
   ```
   Platforms\Android\libs\arm64-v8a\libserialport.so
   ```
2. LibraApp'ni build qiling va test qiling
