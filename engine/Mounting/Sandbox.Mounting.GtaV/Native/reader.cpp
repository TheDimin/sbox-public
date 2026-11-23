#include "dictionary.h"
#include <ragekit/gameindex.h>
#include <ragekit/bounds.h>
#include <ragekit/drawable.h>
#include <ragekit/image.h>
#include <ragekit/fragment.h>
#include <limits>
#include <memory>
#include <stdexcept>
#include <type_traits>

#ifdef _WIN32
#define API extern "C" __declspec(dllexport)
#else
#define API extern "C" __attribute__((visibility("default")))
#endif

namespace {
using namespace rk;
using Bytes = std::vector<uint8_t>;
thread_local std::string error;

struct Packet {
    Bytes bytes;
    template<class T> void put(const T& v) {
        static_assert(std::is_trivially_copyable_v<T>);
        auto p = reinterpret_cast<const uint8_t*>(&v);
        bytes.insert(bytes.end(), p, p + sizeof v);
    }
    void count(size_t n) {
        if (n > INT32_MAX) throw std::length_error("Resource exceeds mount size limit");
        put(static_cast<int32_t>(n));
    }
    void string(const std::string& s) {
        count(s.size());
        bytes.insert(bytes.end(), s.begin(), s.end());
    }
};

enum Kind : int32_t { Scene, Prefab, ModelFile, DictionaryModel, TextureFile, Collision, FragmentFile };
void entry(Packet& out, Kind kind, uint32_t file, uint32_t member, const std::string& name) {
    out.put(kind); out.put(file); out.put(member); out.string(name);
}

void catalog(const GameIndex& index, Packet& out) {
    for (const auto& a : index.archetypes()) {
        auto active = index.archetype(a.name);
        if (active && active->ytyp_file == a.ytyp_file)
            entry(out, Prefab, a.ytyp_file, a.name, index.name(a.name));
    }
    for (uint32_t i = 0; i < index.files().size(); ++i) {
        const auto& f = index.files()[i];
        const auto& ext = index.extension(f);
        if (ext != ".ymap" && ext != ".ydr" && ext != ".ydd" && ext != ".ytd" && ext != ".ybn" && ext != ".yft") continue;
        auto winner = index.resolve_file(f.name_hash, ext);
        if (!winner || winner->archive != f.archive || winner->entry != f.entry) continue;
        auto name = index.file_name(f);
        name.resize(name.size() - ext.size());
        try {
            if (ext == ".ydd" || ext == ".ytd") {
                auto data = index.extract(f);
                bool textures = ext == ".ytd";
                DictionaryReader dictionary(data, textures);
                for (const auto& item : dictionary.members) {
                    auto memberName = item.name.empty() ? index.name(item.hash) : item.name;
                    entry(out, textures ? TextureFile : DictionaryModel, i, item.hash, name + "/" + memberName);
                }
            } else {
                entry(out, ext == ".ymap" ? Scene : ext == ".ydr" ? ModelFile : ext == ".yft" ? FragmentFile : Collision, i, 0, ext == ".yft" ? "fragments/" + name : name);
            }
        } catch (const rk::Error& ex) {
            entry(out, static_cast<Kind>(-2), i, 0, index.path(f) + ": " + ex.what());
        }
    }
}

void collision(Packet& out, const Bound* bound) {
    auto triangles = bound ? bound_triangles(*bound) : std::vector<BoundTriangle>{};
    out.count(triangles.size());
    for (const auto& tri : triangles)
        for (const auto& p : tri.v) out.put(p);
}

Vec3 transformed(const Mat4& m, Vec3 v, float w) {
    auto axis = [&](int i) { return v.x * m.at(0,i) + v.y * m.at(1,i) + v.z * m.at(2,i) + w * m.at(3,i); };
    return {axis(0), axis(1), axis(2)};
}

void meshes(Packet& out, const Drawable& d, const Mat4& transform, const ShaderGroup* inherited = nullptr) {
    for (const auto& lod : d.lods) {
        if (lod.empty()) continue;
        auto group = d.shader_group ? &*d.shader_group : inherited;
        for (const auto& m : lod) {
            if (!(m.render_mask & 1)) continue;
            for (const auto& g : m.geometries) {
                if (!g.vertex_count || g.indices.empty()) continue;
                out.count(g.vertex_count);
                std::string diffuse;
                int32_t bucket = 0;
                if (group && g.shader_index < group->shaders.size()) {
                    const auto& shader = group->shaders[g.shader_index];
                    diffuse = shader.texture_of("DiffuseSampler");
                    if (diffuse.empty()) diffuse = shader.texture_of("TextureSampler_layer0");
                    bucket = shader.render_bucket & 7;
                }
                out.put(bucket);
                out.string(diffuse);
                const auto* embedded = group && group->textures
                    ? group->textures->find(diffuse) : nullptr;
                auto dds = embedded ? write_dds(*embedded) : Bytes{};
                out.count(dds.size());
                out.bytes.insert(out.bytes.end(), dds.begin(), dds.end());
                for (uint32_t i = 0; i < g.vertex_count; ++i) {
                    out.put(transformed(transform, g.position(i), 1)); out.put(transformed(transform, g.normal(i), 0)); out.put(g.uv(i));
                }
                out.count(g.indices.size());
                for (uint16_t i : g.indices) out.put(i);
            }
        }
        break;
    }
}

void model(Packet& out, const Drawable& d) {
    meshes(out, d, Mat4::identity());
    out.count(0);
    collision(out, d.bound.get());
}

void placements(Packet& out, const std::vector<Entity>& entities) {
    out.count(entities.size());
    for (const auto& e : entities) {
        out.put(e.archetype_name);
        out.put(e.position);
        auto orientation = e.rotation;
        if (!e.is_mlo_instance) {
            orientation.x = -orientation.x;
            orientation.y = -orientation.y;
            orientation.z = -orientation.z;
        }
        out.put(orientation);
        out.put(e.scale_xy);
        out.put(e.scale_z);
    }
}
void read(const GameIndex& index, Kind kind, uint32_t file, uint32_t member, Packet& out) {
    if (kind == Prefab) {
        auto a = index.archetype(member);
        if (!a) throw std::runtime_error("Missing archetype");
        auto drawable = index.resolve_drawable(*a);
        std::string path;
        if (drawable.file) {
            path = index.file_name(*drawable.file);
            path.resize(path.size() - 4);
            if (drawable.type == ResolvedDrawable::Yft) path = "fragments/" + path;
            if (drawable.type == ResolvedDrawable::Ydd)
                path += "/" + index.name(drawable.drawable_name);
        }
        out.string(path);
        if (a->kind == IndexedArchetype::Mlo) {
            auto defs = read_ytyp(index.extract(index.files().at(file)));
            for (const auto& archetype : defs.archetypes) {
                if (archetype.name != member) continue;
                placements(out, archetype.entities);
                return;
            }
            throw Error("Interior missing from type dictionary");
        }
        out.count(0);
        return;
    }
    auto data = index.extract(index.files().at(file));
    auto archetype = index.archetype(kind == DictionaryModel ? member : index.files().at(file).name_hash);
    auto txd = archetype ? archetype->texture_dictionary : index.files().at(file).name_hash;
    auto dictionary = index.name(txd);
    if (kind == ModelFile || kind == DictionaryModel || kind == FragmentFile) {
        auto parents = index.txd_parents(txd);
        out.count(parents.size() + 1);
        out.string(dictionary);
        for (auto parent : parents) out.string(index.name(parent));
    }
    switch (kind) {
    case Scene: {
        auto map = read_ymap(data);
        placements(out, map.entities);
        out.count(map.physics_dictionaries.size());
        for (auto hash : map.physics_dictionaries) out.string(index.name(hash));
        break;
    }
    case FragmentFile: {
        auto fragment = read_yft(data);
        const auto* shaders = fragment.drawable && fragment.drawable->shader_group ? &*fragment.drawable->shader_group : nullptr;
        for (const auto& part : fragment.visual_parts()) meshes(out, *part.drawable, part.transform, shaders);
        out.count(0);
        auto& lod = fragment.physics[0];
        auto bound = lod ? lod->bound.get() : nullptr;
        if (!bound && lod && lod->archetype) bound = lod->archetype->bound.get();
        collision(out, bound ? bound : fragment.drawable ? fragment.drawable->bound.get() : nullptr);
        break;
    }
    case ModelFile: model(out, read_ydr(data)); break;
    case DictionaryModel: {
        auto dict = read_ydd(data);
        auto d = dict.find(member);
        if (!d) throw std::runtime_error("Missing drawable in dictionary");
        model(out, *d);
        break;
    }
    case TextureFile: {
        out.bytes = write_dds(DictionaryReader(data, true).texture(member));
        break;
    }
    case Collision: collision(out, read_ybn(data).get()); break;
    default: throw std::invalid_argument("Unknown resource type");
    }
}
}

API void* gta_open(const char* dir) noexcept {
    try { return new rk::GameIndex(rk::GameIndex::open(dir)); }
    catch (const std::exception& ex) { error = ex.what(); return nullptr; }
}
API void gta_close(rk::GameIndex* index) { delete index; }
API const char* gta_index_key(rk::GameIndex* index) { return index->info().key.c_str(); }
API const char* gta_error() { return error.c_str(); }
API void gta_free(Packet* packet) { delete packet; }
API void* gta_read(rk::GameIndex* index, int kind, uint32_t file, uint32_t member,
                  const uint8_t** data, int32_t* size) noexcept {
    try {
        auto packet = std::make_unique<Packet>();
        if (kind == -1) catalog(*index, *packet);
        else read(*index, static_cast<Kind>(kind), file, member, *packet);
        if (packet->bytes.size() > INT32_MAX) throw std::length_error("Resource too large");
        *data = packet->bytes.data();
        *size = static_cast<int32_t>(packet->bytes.size());
        return packet.release();
    } catch (const std::exception& ex) { error = ex.what(); return nullptr; }
}
