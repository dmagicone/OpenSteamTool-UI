#pragma once

#include <filesystem>
#include <string>

// Where OpenSteamTool keeps its files under the Steam directory. Installs upgraded
// from OpenSteamTool still have the legacy names: the data directory is
// migrated once, and the legacy config file is read when no new one exists.
namespace Paths {

    struct DataDirInit {
        bool migrated = false;   // legacy dir was renamed into place
        std::string error;       // rename failed; the legacy dir stays in use
    };

    // Picks <steam>/OpenSteamTool, renaming a legacy <steam>/opensteamtool into
    // place first. Must run before anything opens files under either.
    DataDirInit InitDataDir(const std::filesystem::path& steamDir);

    // Logs, signature cache. Empty until InitDataDir has run.
    const std::filesystem::path& DataDir();

    // <steam>/OpenSteamTool.toml, or <steam>/opensteamtool.toml when only that exists.
    std::filesystem::path ResolveConfigFile(const std::filesystem::path& steamDir);

} // namespace Paths
