# cnc-ddraw 7.1.0.0

- Upstream: https://github.com/FunkyFr3sh/cnc-ddraw
- Binary: https://github.com/FunkyFr3sh/cnc-ddraw/releases/download/v7.1.0.0/cnc-ddraw.zip
- License: MIT, included in LICENSE.txt and distributed as cnc-ddraw-LICENSE.txt.
- ddraw.dll SHA-256: `85E0F7D530DFDA134793A57CB3E76B0287DCC96892EE57162DD68F47283B03A9`
- Upstream ddraw.ini is retained as a reference, not installed as a user configuration.
- Launcher package stores the module as cnc-ddraw.dll. Each enhanced-mode game launch verifies its hash and copies it only beside the generated game EXE as ddraw.dll. The original game directory is not modified.
