/**
 * mesh-bridge firmlet -- example C implementation.
 *
 * This firmlet exposes mesh.v1.MeshService with three methods (Route,
 * Discover, GetTopology). Handler export names are derived from
 * services.tw: handler="mesh_{name:lowercase}" produces mesh_route,
 * mesh_discover, mesh_gettopology.
 *
 * Payload types (RouteRequest, RouteResponse, etc.) are generated from
 * mesh.proto at build time using nanopb.
 *
 * Build:
 *   nanopb_generator mesh.proto
 *   clang --target=wasm32-wasi -O2 -o mesh-bridge.wasm main.c mesh.pb.c
 *   wamrc -o mesh-bridge.aot mesh-bridge.wasm
 */

#include <stdint.h>
#include <string.h>
#include "mesh.pb.h"

/* ── Host function imports ─────────────────────────────────────────────
 *
 * These are provided by the WASM host process (WasmHost) and registered
 * as WAMR native imports. The firmlet calls them to interact with the
 * outside world.
 */

__attribute__((import_module("env"), import_name("tw_log")))
extern void tw_log(int32_t level, const char* msg, uint32_t msg_len);

__attribute__((import_module("env"), import_name("tw_call_service")))
extern int32_t tw_call_service(
    const char* service, uint32_t service_len,
    const char* method,  uint32_t method_len,
    const uint8_t* req,  uint32_t req_len,
    uint8_t* resp,       uint32_t* resp_cap,
    uint32_t timeout_ms);

__attribute__((import_module("env"), import_name("tw_write_measure")))
extern int32_t tw_write_measure(
    const char* name, uint32_t name_len,
    double value);

__attribute__((import_module("env"), import_name("tw_get_time_ms")))
extern int64_t tw_get_time_ms(void);

/* ── Internal state ────────────────────────────────────────────────────*/

#define MAX_NODES 64

static mesh_v1_MeshNode topology[MAX_NODES];
static uint32_t node_count = 0;

/* ── Handlers ──────────────────────────────────────────────────────────
 *
 * Each handler follows the standard signature:
 *   int32_t handler(const uint8_t* req, uint32_t req_len,
 *                   uint8_t* resp, uint32_t* resp_cap);
 *
 * Return: 0 = success, negative = error.
 * On success the handler writes the serialized response into resp and
 * sets *resp_cap to the actual response length.
 */

__attribute__((export_name("mesh_route")))
int32_t mesh_route(const uint8_t* req, uint32_t req_len,
                   uint8_t* resp, uint32_t* resp_cap) {
    mesh_v1_RouteRequest request = mesh_v1_RouteRequest_init_zero;
    pb_istream_t in = pb_istream_from_buffer(req, req_len);
    if (!pb_decode(&in, mesh_v1_RouteRequest_fields, &request))
        return -1;

    /* Simplified routing: look up target in topology, forward payload. */
    int32_t status = -3;  /* not found */
    uint32_t hops = 0;

    for (uint32_t i = 0; i < node_count; i++) {
        if (memcmp(topology[i].device_id.bytes,
                   request.target_device_id.bytes,
                   request.target_device_id.size) == 0) {
            status = 0;
            hops = topology[i].depth;
            break;
        }
    }

    mesh_v1_RouteResponse response = {
        .status = status,
        .hops = hops,
        .latency_ms = hops * 15,
    };
    pb_ostream_t out = pb_ostream_from_buffer(resp, *resp_cap);
    if (!pb_encode(&out, mesh_v1_RouteResponse_fields, &response))
        return -2;

    *resp_cap = out.bytes_written;
    return 0;
}

__attribute__((export_name("mesh_discover")))
int32_t mesh_discover(const uint8_t* req, uint32_t req_len,
                      uint8_t* resp, uint32_t* resp_cap) {
    mesh_v1_DiscoverRequest request = mesh_v1_DiscoverRequest_init_zero;
    pb_istream_t in = pb_istream_from_buffer(req, req_len);
    if (!pb_decode(&in, mesh_v1_DiscoverRequest_fields, &request))
        return -1;

    /*
     * Real implementation would probe the mesh network. This example
     * returns the cached topology filtered by max_depth.
     */
    mesh_v1_DiscoverResponse response = mesh_v1_DiscoverResponse_init_zero;
    response.nodes_count = 0;

    for (uint32_t i = 0; i < node_count && response.nodes_count < MAX_NODES; i++) {
        if (request.max_depth == 0 || topology[i].depth <= request.max_depth) {
            response.nodes[response.nodes_count++] = topology[i];
        }
    }

    pb_ostream_t out = pb_ostream_from_buffer(resp, *resp_cap);
    if (!pb_encode(&out, mesh_v1_DiscoverResponse_fields, &response))
        return -2;

    *resp_cap = out.bytes_written;
    return 0;
}

__attribute__((export_name("mesh_gettopology")))
int32_t mesh_gettopology(const uint8_t* req, uint32_t req_len,
                         uint8_t* resp, uint32_t* resp_cap) {
    (void)req;
    (void)req_len;

    mesh_v1_GetTopologyResponse response = mesh_v1_GetTopologyResponse_init_zero;
    response.links_count = 0;

    for (uint32_t i = 0; i < node_count && response.links_count < MAX_NODES; i++) {
        if (topology[i].parent_id.size > 0) {
            mesh_v1_MeshLink* link = &response.links[response.links_count++];
            memcpy(link->source_id.bytes, topology[i].parent_id.bytes,
                   topology[i].parent_id.size);
            link->source_id.size = topology[i].parent_id.size;
            memcpy(link->target_id.bytes, topology[i].device_id.bytes,
                   topology[i].device_id.size);
            link->target_id.size = topology[i].device_id.size;
            link->rssi = topology[i].rssi;
            link->latency_ms = 15;
        }
    }

    pb_ostream_t out = pb_ostream_from_buffer(resp, *resp_cap);
    if (!pb_encode(&out, mesh_v1_GetTopologyResponse_fields, &response))
        return -2;

    *resp_cap = out.bytes_written;
    return 0;
}
