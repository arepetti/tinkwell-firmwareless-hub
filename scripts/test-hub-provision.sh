#!/usr/bin/env bash
# test-hub-provision.sh -- Full provisioning flow: discover device, install
# firmlet, register with hub, and start the WASM host.
#
# Usage:
#   ./scripts/test-hub-provision.sh <device-addr> <firmlet-name> [version]
#   ./scripts/test-hub-provision.sh 192.168.4.1 myFirmlet 1.0.0

set -euo pipefail

DEVICE_ADDR="${1:?Usage: $0 <device-addr> <firmlet-name> [version]}"
FIRMLET_NAME="${2:?Usage: $0 <device-addr> <firmlet-name> [version]}"
FIRMLET_VERSION="${3:-}"
FIRMLET_BASE_DIR="${TW_FIRMLET_BASE_DIR:-/var/lib/tinkwell/firmlets}"

echo "==> Step 1: Provisioning device at ${DEVICE_ADDR}..."
PROVISION_OUTPUT=$(tw hub provision --device-addr "${DEVICE_ADDR}" --format jsonl)
ASSET_ID=$(echo "${PROVISION_OUTPUT}" | python3 -c "import sys,json; print(json.load(sys.stdin)['assetId'])" 2>/dev/null || echo "")

if [ -z "${ASSET_ID}" ]; then
    echo "==> Provision output:"
    echo "${PROVISION_OUTPUT}"
    echo ""
    echo "Enter the asset ID from the output above:"
    read -r ASSET_ID
fi

INSTALL_DIR="${FIRMLET_BASE_DIR}/${ASSET_ID}"
mkdir -p "${INSTALL_DIR}"

echo "==> Asset ID:    ${ASSET_ID}"
echo "==> Install dir: ${INSTALL_DIR}"
echo ""

# Step 2: Install firmlet
echo "==> Step 2: Installing firmlet ${FIRMLET_NAME}..."
VERSION_FLAG=""
if [ -n "${FIRMLET_VERSION}" ]; then
    VERSION_FLAG="--version ${FIRMLET_VERSION}"
fi

tw firmlet-registry download "${FIRMLET_NAME}" \
    -o "${INSTALL_DIR}/package.zip" \
    ${VERSION_FLAG}

cd "${INSTALL_DIR}"
unzip -o package.zip
rm -f package.zip
cd -

echo ""

# Step 3: Add to hub
echo "==> Step 3: Adding asset to hub..."
tw hub add "${INSTALL_DIR}" --asset-id "${ASSET_ID}"

echo ""
echo "==> Done! Device ${DEVICE_ADDR} provisioned as asset ${ASSET_ID}."
echo "To remove:  tw hub remove ${ASSET_ID}"
