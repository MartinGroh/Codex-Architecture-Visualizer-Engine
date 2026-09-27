#include "cave_agent_flow.h"
#include "poll_example.h"
#include <type_traits>
static_assert(std::is_standard_layout<cave_flow_snapshot>::value, "Snapshot must remain usable by C and C++ callers");
extern "C" int cave_flow_cpp_smoke(void) {
    return cave_flow_result_name(CAVE_FLOW_OK)[0] == 'R' ? 1 : 0;
}
