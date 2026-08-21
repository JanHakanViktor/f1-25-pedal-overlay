const path = require("node:path");

const iconPath = path.join(__dirname, "assets", "app-icon");

module.exports = {
  packagerConfig: {
    asar: true,
    icon: iconPath,
    ignore: [/[\\/]release(?:[\\/]|$)/]
  },
  rebuildConfig: {},
  makers: [
    {
      name: "@electron-forge/maker-squirrel",
      platforms: ["win32"],
      config: {
        name: "F125PedalOverlay",
        authors: "JanHakanViktor",
        description: "Transparent throttle and brake input overlay for EA SPORTS F1 25",
        exe: "F1 25 Pedal Overlay.exe",
        setupExe: "F1-25-Pedal-Overlay-Setup.exe",
        setupIcon: `${iconPath}.ico`,
        noMsi: true
      }
    }
  ]
};
