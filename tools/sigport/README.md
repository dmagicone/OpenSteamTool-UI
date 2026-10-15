# sigport

Keeps OpenSteamTool's per-build signature files current. Given the previous Steam
client build and its TOML files, it finds every function and IPC wrapper in a
new build and writes the new files. The scheduled job in
[Smokestack](https://github.com/SvnFrs/smokestack) runs it every few hours.

Requires [Bun](https://bun.sh) 1.3+.

```sh
bun install
bun test

# End to end against a Smokestack checkout (pattern/ and ipc/ branch worktrees):
bun src/cli.ts update --data ../data --report report.md
# exit 0 = done, 2 = files written but some functions need review

# Manual tools
bun src/cli.ts fetch --out ./bins                     # current client DLLs from Valve's CDN
bun src/cli.ts port-pattern --old-dll old/steamclient64.dll --new-dll bins/steamclient64.dll --old-toml old.toml --out new.toml
bun src/cli.ts port-ipc --new-dll bins/steamclient64.dll --old-toml old-ipc.toml --out new-ipc.toml
bun src/cli.ts verify --dll bins/steamclient64.dll --toml new.toml
```

## How a function is found

For each entry in the old file, candidates in the new binary come from the old
signature's hits, falling back to voting with 16-byte windows of the old
function. Each candidate is graded against the old function, decoded with
[iced-x86](https://github.com/icedland/iced):

| grade     | meaning                                                         |
|-----------|-----------------------------------------------------------------|
| `exact`   | identical code apart from relinked addresses                    |
| `shape`   | identical instructions; only constants moved (struct offsets, frame size) |
| `strings` | at least 60% of the old function's string references are shared |
| `sig`     | the old signature still matches exactly once                    |
| `votes`   | located by byte-window voting only; **needs review**            |
| `failed`  | not found; **needs review**, entry is left out                  |

The chosen function then gets a signature that is unique in the new build.
Address fields are wildcarded (RIP-relative displacements and call/jump
targets), and so are struct offsets and stack-frame sizes, so the signature
tends to survive the next update too. If the old signature is still unique it
is kept, so the files change as little as possible.

IPC specs keep `funcHash`, `fencepost` and `argc` (properties of the method
signature). Each interface's vtable is located by finding functions that embed
the method hashes and the vtable slots pointing at them.
