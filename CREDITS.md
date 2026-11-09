# Credits and assets

The project code, owner's Blender turtle, prepared runtime mesh, code-generated workstation pattern, and app icon are provided under the project MIT license. Minecraft, Windows, ChatGPT, and Codex names identify inspiration or compatibility; this unofficial fan project is not affiliated with Mojang, Microsoft, or OpenAI. The project does not claim ownership of Minecraft's underlying character or trademark rights.

## Active assets

| File | Source and use |
| --- | --- |
| `assets/baby-turtle.blend` | Owner-authored separated Blender turtle; prepared copy with six attachment pivots and flat-color materials |
| `assets/baby-turtle.mesh.json` | Export of that Blender geometry and material colors for the desktop renderer |
| `assets/turtle.ico` | Project-generated turtle app icon |

The prepared Blender file has no embedded images, text scripts, or linked libraries. The runtime model uses material colors rather than bitmap textures. The workstation's wood pattern and the emergency fallback turtle are generated in code; no old texture-reference images are loaded.

Behavior inspiration: [gprem09/minecraft_pet_v2](https://github.com/gprem09/minecraft_pet_v2). This is an independent native Windows implementation; no source files from that repository are bundled here.

## Retired development references

The downloaded MakerWorld STL, JustPapercraft reference sheet, appearance-reference image, and supplied oak image have been removed from the current project assets and release packaging. The STL's earlier MIT attribution was incorrect; the owner identified a Standard Digital File License. These retired third-party files are not granted rights under the project's MIT license.

The prior STL source was the [MakerWorld Baby Turtle model](https://makerworld.com/en/models/2563054-baby-turtle-minecraft-tiny-takeover-no-ams-glue#profileId-2824289); the sheet bore JustPapercraft credit (`ko-fi.com/justpapercraft`, `@justpapercraft`). These links record development provenance, not bundled assets or permission to redistribute them.

Working-tree cleanup does not remove files from previously published Git commits or remotely uploaded release assets. Git history has not been rewritten. Earlier Fusion experiments are retained locally under ignored `cad/` and are not part of the current app or release package.
