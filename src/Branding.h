#pragma once

// Product identity. OpenSteamTool is a fork of OpenSteamTool; the legacy names are
// what OpenSteamTool installs used on disk and are still honoured so an
// upgrade in place keeps working (see Utils/Config/Paths.h).
namespace Branding {

    inline constexpr const char* kProductName = "OpenSteamTool";

    inline constexpr const char* kDataDirName    = "OpenSteamTool";        // <Steam>/OpenSteamTool/
    inline constexpr const char* kConfigFileName = "OpenSteamTool.toml";   // <Steam>/OpenSteamTool.toml

    inline constexpr const char* kLegacyDataDirName    = "opensteamtool";
    inline constexpr const char* kLegacyConfigFileName = "opensteamtool.toml";

    inline constexpr const char* kIssuesUrl = "https://github.com/SvnFrs/OpenSteamTool/issues";

    // Per-build signature data, published by tools/sigport to the `pattern`
    // and `ipc` branches.
    inline constexpr const char* kSignaturesRepoUrl = "https://github.com/SvnFrs/smokestack";

} // namespace Branding
