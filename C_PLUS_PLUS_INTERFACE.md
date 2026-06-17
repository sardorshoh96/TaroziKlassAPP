# C++ Shared Library Interface

## TG2480H Printer uchun C++ Shared Library Interfeysi

C# kod to'g'ridan-to'g'ri C++ shared library'ni P/Invoke orqali chaqiradi. Quyidagi funksiyalar talab qilinadi:

### Talab qilinadigan funksiyalar:

#### 1. serialport_open
```cpp
// Port'ni ochish
// Return: handle (>= 0) yoki -1 (xatolik)
int serialport_open(const char* port, int baudRate);
```

#### 2. serialport_close
```cpp
// Port'ni yopish
void serialport_close(int handle);
```

#### 3. serialport_write
```cpp
// Ma'lumot yozish
// Return: yozilgan byte'lar soni yoki -1 (xatolik)
int serialport_write(int handle, const unsigned char* data, int length);
```

#### 4. serialport_read
```cpp
// Ma'lumot o'qish
// Return: o'qilgan byte'lar soni yoki -1 (xatolik)
int serialport_read(int handle, unsigned char* buffer, int length);
```

#### 5. serialport_is_open (ixtiyoriy)
```cpp
// Port ochilganligini tekshirish
// Return: 1 (ochiq) yoki 0 (yopiq)
int serialport_is_open(int handle);
```

### Library nomi:
- Library nomi: `serialport`
- Fayl nomi: `libserialport.so` (Android uchun)

### Joylashuvi:
- `Platforms/Android/libs/armeabi-v7a/libserialport.so`
- `Platforms/Android/libs/arm64-v8a/libserialport.so`
- `Platforms/Android/libs/x86/libserialport.so`
- `Platforms/Android/libs/x86_64/libserialport.so`

### C++ kod namunasi:

```cpp
#include <fcntl.h>
#include <termios.h>
#include <unistd.h>
#include <string.h>

extern "C" {
    int serialport_open(const char* port, int baudRate) {
        int fd = open(port, O_RDWR | O_NOCTTY | O_NDELAY);
        if (fd < 0) return -1;
        
        // Termios sozlamalari...
        // Baud rate o'rnatish...
        
        return fd; // Handle sifatida fd'ni qaytarish
    }
    
    void serialport_close(int handle) {
        if (handle >= 0) {
            close(handle);
        }
    }
    
    int serialport_write(int handle, const unsigned char* data, int length) {
        if (handle < 0) return -1;
        return write(handle, data, length);
    }
    
    int serialport_read(int handle, unsigned char* buffer, int length) {
        if (handle < 0) return -1;
        return read(handle, buffer, length);
    }
    
    int serialport_is_open(int handle) {
        return handle >= 0 ? 1 : 0;
    }
}
```

### Eslatmalar:
- Barcha funksiyalar `extern "C"` blokida bo'lishi kerak
- Calling convention: Cdecl
- String encoding: ANSI (char*)
- Handle: int (file descriptor)

