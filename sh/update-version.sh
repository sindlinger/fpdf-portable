#!/bin/bash

# Script to update version across all files
# Usage: ./update-version.sh 3.9.0

if [ -z "$1" ]; then
    echo "Usage: ./update-version.sh <new-version>"
    echo "Example: ./update-version.sh 3.9.0"
    exit 1
fi

NEW_VERSION=$1
echo "Updating to version $NEW_VERSION..."

# Update Version.cs
sed -i "s/public const string Current = \".*\"/public const string Current = \"$NEW_VERSION\"/" src/Version.cs

# Update fpdf.csproj
sed -i "s/<Version>.*<\/Version>/<Version>$NEW_VERSION<\/Version>/" fpdf.csproj
sed -i "s/<AssemblyVersion>.*<\/AssemblyVersion>/<AssemblyVersion>$NEW_VERSION.0<\/AssemblyVersion>/" fpdf.csproj
sed -i "s/<FileVersion>.*<\/FileVersion>/<FileVersion>$NEW_VERSION.0<\/FileVersion>/" fpdf.csproj

# Update context files
sed -i "s/\"version\": \".*\"/\"version\": \"$NEW_VERSION\"/" .context/AGENT_MEMORY.json
sed -i "s/- \*\*Version\*\*: .*/- **Version**: $NEW_VERSION/" .context/QUICK_REFERENCE.md
sed -i "s/Version: .*/Version: $NEW_VERSION/" CLAUDE.md

# Update build.sh
sed -i "s/Building FilterPDF v.*/Building FilterPDF v$NEW_VERSION.../" build.sh

echo "Version updated to $NEW_VERSION in all files"
echo ""
echo "Next steps:"
echo "1. Update release notes in src/Version.cs"
echo "2. Run: ./build.sh"
echo "3. Run: ./install.sh"
echo "4. Commit changes"