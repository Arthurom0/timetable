#!/bin/bash
# Builds Timetable.app on the Mac itself (Apple silicon Macs get a native arm64 app).
# Needs Apple's free command line tools; if they are missing, macOS offers to install them.
set -e
cd "$(dirname "$0")"

if ! xcode-select -p >/dev/null 2>&1; then
  echo "Apple's command line tools are needed. A window will offer to install them."
  echo "When that has finished, run this script again."
  xcode-select --install
  exit 1
fi

APP="Timetable.app"
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"

echo "Compiling..."
swiftc -O main.swift -o "$APP/Contents/MacOS/Timetable" -framework Cocoa -framework WebKit

cp Timetable.html Timetable.icns "$APP/Contents/Resources/"

cat > "$APP/Contents/Info.plist" <<'EOF'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>Timetable</string>
  <key>CFBundleDisplayName</key><string>Timetable</string>
  <key>CFBundleIdentifier</key><string>local.timetable.app</string>
  <key>CFBundleExecutable</key><string>Timetable</string>
  <key>CFBundleIconFile</key><string>Timetable</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>1.0</string>
  <key>CFBundleVersion</key><string>1</string>
  <key>LSMinimumSystemVersion</key><string>12.0</string>
  <key>NSHighResolutionCapable</key><true/>
</dict>
</plist>
EOF

# Files on the Desktop pick up extended attributes that codesign refuses to sign
xattr -cr "$APP"
codesign --force --sign - "$APP"

echo "Done: $(pwd)/$APP"
echo "Drag Timetable.app into your Applications folder."
open -R "$APP"
