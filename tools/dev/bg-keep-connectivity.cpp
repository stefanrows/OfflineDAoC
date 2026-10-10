// Battleground keep connectivity for the navigation builder output (run by tools/dev/build-battleground-nav.sh).
//
// Usage: bg-keep-connectivity <zoneNNN.nav> <zone-id> <input.txt> <output.txt>
//
// Input lines (written from tools/dev/battleground-keeps.json):
//   K <zone> <region> <keepId> <kind> <x> <y> <z>   a battleground keep centre (kind = central or portal)
//   L <region> <landingId> <x> <y> <z>               a realm landing of a region (its portal keep centre)
//
// For each K of this zone and each L of the same region with another id, the floor at the landing is searched for a
// path to the floor at the keep centre. Filters: default (the server's DefaultFilters), blocking (the server's
// BlockingDoorAvoidance, runtime-closed doors), and gates_closed (Door-area polygons excluded, i.e. every keep gate shut).
// Each is checked unbounded (connectivity) and default_capped with the server's 256-polygon corridor per call. PASS
// means a complete path; UNKNOWN means the query ran out of nodes; FAIL and NO_FLOOR fail the battleground check.
//
// Build with -DDT_POLYREF64 and the Detour sources in source/development-tools/OpenDAoC-Core/Pathing/Detour (the same
// dtPolyRef width the server and Detour.dll use). The helper compiles it with g++ into the developer output tree.
#include <cstdio>
#include <cstdlib>
#include <fstream>
#include <sstream>
#include <string>
#include <vector>
#include "DetourInterface.hpp"
#include "DetourNavMesh.h"
#include "DetourNavMeshQuery.h"
#include "DetourStatus.h"

namespace
{
    const unsigned short FLAG_DISABLED = 0x10;
    const unsigned short FLAG_DOOR = 0x04;
    const unsigned short FLAG_BLOCKING_DOOR = 0x20;
    const int SERVER_CORRIDOR_CAP = 256;
    const int FLOOD_RESULTS = 200000;
    const int NODE_POOL = 65535;

    struct Point { double x, y, z; };
    struct Keep { int zone, region, keepId; std::string kind; Point p; };
    struct Landing { int region, id; Point p; };

    enum class Outcome { Pass, Fail, Unknown };

    void ToRecast(const Point& p, float r[3]) { r[0] = (float)(p.x / 32.0); r[1] = (float)(p.z / 32.0); r[2] = (float)(p.y / 32.0); }
    Point FromRecast(const float r[3]) { return Point{ r[0] * 32.0, r[2] * 32.0, r[1] * 32.0 }; }

    // Game half extents (x/y horizontal, z height) in recast order (x, height, z).
    void Extents(double hx, double hz, float e[3]) { e[0] = (float)(hx / 32.0); e[1] = (float)(hz / 32.0); e[2] = (float)(hx / 32.0); }

    dtQueryFilter MakeFilter(unsigned short exclude)
    {
        dtQueryFilter f;
        f.setIncludeFlags(0xFFFF ^ FLAG_DISABLED);
        f.setExcludeFlags(exclude);
        return f;
    }

    bool Snap(dtNavMeshQuery* q, const Point& p, double hx, double hz, dtPolyRef& ref, Point& floor)
    {
        float r[3], e[3], n[3];
        ToRecast(p, r);
        Extents(hx, hz, e);
        dtQueryFilter f = MakeFilter(0);
        ref = 0;
        if (dtStatusFailed(q->findNearestPoly(r, e, &f, &ref, n)) || ref == 0) return false;
        floor = FromRecast(n);
        return true;
    }

    // Unbounded connectivity: a Dijkstra flood from the landing floor reaches every polygon of its component. A* would
    // exhaust the node pool when the keep is in another component, so the flood is the reliable answer here.
    Outcome Reach(dtNavMeshQuery* q, dtPolyRef start, const Point& sp, dtPolyRef end, unsigned short exclude)
    {
        float origin[3];
        ToRecast(sp, origin);
        dtQueryFilter f = MakeFilter(exclude);
        std::vector<dtPolyRef> refs((size_t)FLOOD_RESULTS);
        int count = 0;
        dtStatus st = q->findPolysAroundCircle(start, origin, 1e6f, &f, refs.data(), nullptr, nullptr, &count, FLOOD_RESULTS);
        if (dtStatusFailed(st)) return Outcome::Fail;
        if ((st & DT_OUT_OF_NODES) || count >= FLOOD_RESULTS) return Outcome::Unknown;
        for (int k = 0; k < count; ++k)
            if (refs[k] == end) return Outcome::Pass;
        return Outcome::Fail;
    }

    // Server corridor semantics: one findPath with at most maxPolys corridor polygons (the server uses 256).
    Outcome Capped(dtNavMeshQuery* q, dtPolyRef start, const Point& sp, dtPolyRef end, const Point& ep, unsigned short exclude, int maxPolys)
    {
        float a[3], b[3];
        ToRecast(sp, a);
        ToRecast(ep, b);
        dtQueryFilter f = MakeFilter(exclude);
        std::vector<dtPolyRef> polys((size_t)maxPolys);
        int count = 0;
        dtStatus st = q->findPath(start, end, a, b, &f, polys.data(), &count, maxPolys);
        if (st & DT_OUT_OF_NODES) return Outcome::Unknown;
        if (dtStatusFailed(st) || count <= 0) return Outcome::Fail;
        bool reached = polys[count - 1] == end && (st & DT_PARTIAL_RESULT) == 0;
        return reached ? Outcome::Pass : Outcome::Fail;
    }

    const char* Name(Outcome o)
    {
        return o == Outcome::Pass ? "PASS" : o == Outcome::Unknown ? "UNKNOWN" : "FAIL";
    }
}

int main(int argc, char** argv)
{
    if (argc != 5)
    {
        std::fprintf(stderr, "usage: bg-keep-connectivity <zoneNNN.nav> <zone-id> <input.txt> <output.txt>\n");
        return 1;
    }
    const char* navPath = argv[1];
    const int zone = std::atoi(argv[2]);
    const char* inputPath = argv[3];
    const char* outputPath = argv[4];

    std::ofstream out(outputPath);
    dtNavMesh* mesh = nullptr;
    dtNavMeshQuery* q = nullptr;
    if (!LoadNavMesh(navPath, &mesh) || mesh == nullptr || !CreateNavMeshQuery(mesh, &q) || q == nullptr || dtStatusFailed(q->init(mesh, NODE_POOL)))
    {
        out << "zone=" << zone << " status=navmesh_not_loaded\n";
        return 2;
    }

    std::vector<Keep> keeps;
    std::vector<Landing> landings;
    std::ifstream in(inputPath);
    std::string line;
    while (std::getline(in, line))
    {
        std::istringstream ss(line);
        std::string tag;
        ss >> tag;
        if (tag == "K")
        {
            Keep k;
            ss >> k.zone >> k.region >> k.keepId >> k.kind >> k.p.x >> k.p.y >> k.p.z;
            if (ss && k.zone == zone) keeps.push_back(k);
        }
        else if (tag == "L")
        {
            Landing l;
            ss >> l.region >> l.id >> l.p.x >> l.p.y >> l.p.z;
            if (ss) landings.push_back(l);
        }
    }

    int pairs = 0, defaultFailures = 0, gatedFailures = 0, unknown = 0;
    for (const Keep& k : keeps)
    {
        dtPolyRef endRef = 0;
        Point endFloor{};
        bool endOk = Snap(q, k.p, 128, 256, endRef, endFloor);
        for (const Landing& l : landings)
        {
            if (l.region != k.region || l.id == k.keepId) continue;
            ++pairs;
            dtPolyRef startRef = 0;
            Point startFloor{};
            bool startOk = Snap(q, l.p, 100, 200, startRef, startFloor);
            std::string d = "NO_FLOOR", bl = "NO_FLOOR", g = "NO_FLOOR", c = "NO_FLOOR";
            if (startOk && endOk)
            {
                d = Name(Reach(q, startRef, startFloor, endRef, 0));
                bl = Name(Reach(q, startRef, startFloor, endRef, FLAG_BLOCKING_DOOR));
                g = Name(Reach(q, startRef, startFloor, endRef, FLAG_DOOR));
                c = Name(Capped(q, startRef, startFloor, endRef, endFloor, 0, SERVER_CORRIDOR_CAP));
            }
            if (d == "FAIL" || d == "NO_FLOOR") ++defaultFailures;
            if (d == "UNKNOWN") ++unknown;
            if (g != "PASS") ++gatedFailures;
            out << "zone=" << zone << " region=" << k.region << " keep=" << k.keepId << " kind=" << k.kind << " from_landing=" << l.id
                << " default=" << d << " blocking=" << bl << " gates_closed=" << g << " default_capped=" << c << "\n";
        }
    }
    out << "zone=" << zone << " pairs=" << pairs << " default_failures=" << defaultFailures
        << " gates_closed_failures=" << gatedFailures << " unknown=" << unknown << "\n";

    FreeNavMeshQuery(q);
    FreeNavMesh(mesh);
    return 0;
}
