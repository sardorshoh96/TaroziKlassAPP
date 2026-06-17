# C++ Shared Library Build Instructions

## TG2480H Printer uchun C++ Shared Library

### Talablar:
1. Android NDK (r21 yoki yuqori)
2. CMake yoki Android.mk

### C++ Library Interfeysi:
`C_PLUS_PLUS_INTERFACE.md` faylida batafsil ma'lumot mavjud.

### Talab qilinadigan funksiyalar:
- `serialport_open(const char* port, int baudRate)` -> int handle
- `serialport_close(int handle)` -> void
- `serialport_write(int handle, const unsigned char* data, int length)` -> int
- `serialport_read(int handle, unsigned char* buffer, int length)` -> int
- `serialport_is_open(int handle)` -> int (ixtiyoriy)

### Build qilish:

#### Usul 1: CMake orqali

```cmake
cmake_minimum_required(VERSION 3.18.1)
project("serialport")

add_library(serialport SHARED
    serialport.cpp  # Sizning C++ kod faylingiz
)

target_link_libraries(serialport
    android
    log
)
```

#### Usul 2: Android.mk orqali

```makefile
LOCAL_PATH := $(call my-dir)

include $(CLEAR_VARS)

LOCAL_MODULE := serialport
LOCAL_SRC_FILES := serialport.cpp
LOCAL_LDLIBS := -llog
LOCAL_CFLAGS := -Wall -Wextra

include $(BUILD_SHARED_LIBRARY)
```

### Build komandalari:

```bash
# NDK path'ni o'rnating
export NDK_HOME=/path/to/android-ndk

# Build qilish
cd Platforms/Android/jni  # yoki sizning C++ loyha papkangiz
$NDK_HOME/ndk-build

# Yoki CMake ishlatib:
mkdir build && cd build
cmake -DCMAKE_TOOLCHAIN_FILE=$NDK_HOME/build/cmake/android.toolchain.cmake \
      -DANDROID_ABI=armeabi-v7a \
      -DANDROID_PLATFORM=android-21 \
      ..
cmake --build .
```

### Yaratilgan fayllar:
Yaratilgan `.so` fayllarni quyidagi papkalarga qo'ying:
- `Platforms/Android/libs/armeabi-v7a/libserialport.so`
- `Platforms/Android/libs/arm64-v8a/libserialport.so`
- `Platforms/Android/libs/x86/libserialport.so`
- `Platforms/Android/libs/x86_64/libserialport.so`

### Eslatmalar:
- Library nomi: `libserialport.so` bo'lishi kerak
- Barcha funksiyalar `extern "C"` blokida bo'lishi kerak
- Calling convention: Cdecl
- C# kod avtomatik ravishda library'ni yuklaydi
