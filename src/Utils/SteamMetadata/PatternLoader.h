#pragma once

#include "OSTPlatform/include/DynamicLibrary.h"

#include <string>

namespace PatternLoader {

    // Load metadata before installing hooks for a module. Without a file for
    // the running build, falls back to the newest cached file of another build
    // (signatures only, unique matches only).
    bool Load(OSTPlatform::DynamicLibrary::ModuleHandle module, const std::string& dllPath, const std::string& component);

    // Resolve by RVA first, then fall back to signature scanning.
    void* FindPattern(OSTPlatform::DynamicLibrary::ModuleHandle module, const char* funcName);

    // Report unresolved functions after all hooks have been installed.
    void ReportMissingFunctions();

} // namespace PatternLoader
