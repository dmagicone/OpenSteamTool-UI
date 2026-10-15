#pragma once
#include <string>

namespace RemoteToml {

    struct Request {
        std::string channel;    // "pattern" or "ipc"
        std::string component;  // "steamclient" or "steamui"
        std::string dllPath;
    };

    struct Result {
        bool        ok        = false;
        bool        fromCache = false;
        std::string body;
        std::string sha256;     // of the file the body belongs to
    };

    // Fetch remote TOML first, then fall back to the exact local cache entry.
    Result Fetch(const Request& request);

    // Newest cached file for this channel/component whose sha256 is not
    // `excludeSha256`: data for a *different* build, for when none exists for
    // the running one. Callers may only use build-independent parts of it
    // (signatures, IPC hashes) — its RVAs belong to the other build.
    Result FetchNewestCached(const Request& request, const std::string& excludeSha256);

} // namespace RemoteToml
