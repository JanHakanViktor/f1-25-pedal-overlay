module.exports = {
  packagerConfig: {
    asar: true
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
        noMsi: true
      }
    }
  ]
};
