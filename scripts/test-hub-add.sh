#!/usr/bin/env bash
# test-hub-add.sh -- End-to-end test: install a firmlet from the registry,
# then register it with the hub and start the WASM host.
#
# Prerequisites:
#   - `tw` CLI is built and on PATH
#   - Proxy and AssetRegistry runlets are running
#   - Firmlet registry is accessible (set TW_FIRMLET_REGISTRY_URL)
#
# Usage:
#   ./scripts/test-hub-add.sh <firmlet-name> [version]
#   ./scripts/test-hub-add.sh myFirmlet 1.0.0
#   ./scripts/test-hub-add.sh myFirmlet            # uses latest

set -euo pipefail

FIRMLET_NAME="${1:?Usage: $0 <firmlet-name> [version]}"
FIRMLET_VERSION="${2:-}"
FIRMLET_BASE_DIR="${TW_FIRMLET_BASE_DIR:-/var/lib/tinkwell/firmlets}"
ASSET_ID="$(tw id)"

INSTALL_DIR="${FIRMLET_BASE_DIR}/${ASSET_ID}"
mkdir -p "${INSTALL_DIR}"

echo "==> Asset ID:    ${ASSET_ID}"
echo "==> Firmlet:     ${FIRMLET_NAME}@${FIRMLET_VERSION:-latest}"
echo "==> Install dir: ${INSTALL_DIR}"
echo ""

# Step 1: Download and install the firmlet package
echo "==> Step 1: Installing firmlet from registry..."
VERSION_FLAG=""
if [ -n "${FIRMLET_VERSION}" ]; then
    VERSION_FLAG="--version ${FIRMLET_VERSION}"
fi

tw firmlet-registry download "${FIRMLET_NAME}" \
    -o "${INSTALL_DIR}/package.zip" \
    ${VERSION_FLAG}

# Extract the package
cd "${INSTALL_DIR}"
unzip -o package.zip
rm -f package.zip
cd -

echo "==> Firmlet installed to ${INSTALL_DIR}"
echo ""

# Step 2: Register with hub and start host
echo "==> Step 2: Adding asset to hub..."
tw hub add "${INSTALL_DIR}" --asset-id "${ASSET_ID}"

echo ""
echo "==> Done! Asset ${ASSET_ID} is running."
echo ""
echo "To remove:  tw hub remove ${ASSET_ID}"
