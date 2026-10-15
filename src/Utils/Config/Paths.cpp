#include "Paths.h"
#include "Branding.h"

namespace fs = std::filesystem;

namespace Paths {
namespace {

    // Written once by InitDataDir on the init thread, before any reader runs.
    fs::path g_dataDir;

} // namespace

DataDirInit InitDataDir(const fs::path& steamDir)
{
    DataDirInit result;
    const fs::path dir    = steamDir / Branding::kDataDirName;
    const fs::path legacy = steamDir / Branding::kLegacyDataDirName;
    g_dataDir = dir;

    std::error_code ec;
    if (!fs::exists(dir, ec) && fs::is_directory(legacy, ec)) {
        fs::rename(legacy, dir, ec);
        if (ec) {
            // Keep everything in one place rather than splitting logs and
            // cache across two directories.
            result.error = ec.message();
            g_dataDir = legacy;
        } else {
            result.migrated = true;
        }
    }
    return result;
}

const fs::path& DataDir()
{
    return g_dataDir;
}

fs::path ResolveConfigFile(const fs::path& steamDir)
{
    const fs::path current = steamDir / Branding::kConfigFileName;
    const fs::path legacy  = steamDir / Branding::kLegacyConfigFileName;
    std::error_code ec;
    if (!fs::exists(current, ec) && fs::exists(legacy, ec))
        return legacy;
    return current;
}

} // namespace Paths
