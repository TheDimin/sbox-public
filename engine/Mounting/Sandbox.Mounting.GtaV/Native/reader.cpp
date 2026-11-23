#include "dictionary.h"
#include <algorithm>
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

enum Kind : int32_t { Scene, Prefab, ModelFile, DictionaryModel, TextureFile, Collision, FragmentFile, FragmentGroup };
void entry(Packet& out, Kind kind, uint32_t file, uint32_t member, const std::string& name) {
    out.put(kind); out.put(file); out.put(member); out.string(name);
}

bool detachable(const Fragment& f, size_t group) {
    const auto& lod = *f.physics[0];
    auto strength = lod.groups.at(group).strength();
    if (lod.groups[group].parent_index() == 255 || !std::isfinite(strength) || strength < 0) return false;
    for (const auto& child : lod.children) {
        if (child.group_index != group) continue;
        if (child.drawable) return true;
        if (!f.drawable || !f.drawable->skeleton) continue;
        auto& bones = f.drawable->skeleton->bones;
        for (const auto& models : f.drawable->lods)
            for (const auto& m : models)
                if (m.bone_index() < bones.size() && bones[m.bone_index()].tag == child.bone_tag)
                    return true;
    }
    return false;
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
                if (ext == ".yft" && index.archetype(f.name_hash)) {
                    entry(out, FragmentFile, i, 0, "fragments/" + name);
                    auto fragment = read_yft(index.extract(f));
                    if (fragment.physics[0]) {
                        for (uint32_t g = 0; g < fragment.physics[0]->groups.size(); ++g)
                            if (detachable(fragment, g))
                                entry(out, FragmentGroup, i, g, "fragments/" + name + "/gibs/" + std::to_string(g));
                    }
                    continue;
                }
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

void meshes(Packet& out, const Drawable& d, const std::string& dictionary, const Mat4& placement, int bone = -1, const Fragment* fragment = nullptr, const Drawable* parent = nullptr) {
    auto group = d.shader_group ? &*d.shader_group : parent && parent->shader_group ? &*parent->shader_group : nullptr;
    for (const auto& lod : d.lods) {
        if (lod.empty()) continue;
        for (const auto& m : lod) {
            if (!(m.render_mask & 1) || (bone >= 0 && m.bone_index() != bone)) continue;
            auto transform = placement;
            if (fragment && !m.has_skin()) {
                size_t b = m.bone_index();
                if (b < fragment->bone_transforms.size()) {
                    for (int c = 0; c < 3; ++c) for (int r = 0; r < 4; ++r)
                        transform.at(r, c) = fragment->bone_transforms[b][c * 4 + r];
                } else if (d.skeleton && b < d.skeleton->bones.size()) {
                    transform = d.skeleton->bind_matrix(b);
                }
            }
            for (const auto& g : m.geometries) {
                if (!g.vertex_count || g.indices.empty()) continue;
                out.count(g.vertex_count);
                std::string diffuse, normal;
                int32_t bucket = 0;
                if (group && g.shader_index < group->shaders.size()) {
                    const auto& shader = group->shaders[g.shader_index];
                    diffuse = shader.texture_of("DiffuseSampler");
                    if (diffuse.empty()) diffuse = shader.texture_of("TextureSampler_layer0");
                    normal = shader.texture_of("BumpSampler");
                    if (normal.empty()) normal = shader.texture_of("BumpSampler_layer0");
                    bucket = shader.render_bucket & 7;
                }
                out.put(bucket);
                for (const auto* name : {&diffuse, &normal}) {
                    out.string(*name);
                    const auto* embedded = group && group->textures ? group->textures->find(*name) : nullptr;
                    auto dds = embedded ? write_dds(*embedded) : Bytes{};
                    out.count(dds.size());
                    out.bytes.insert(out.bytes.end(), dds.begin(), dds.end());
                }
                for (uint32_t i = 0; i < g.vertex_count; ++i) {
                    out.put(transformed(transform, g.position(i), 1)); out.put(transformed(transform, g.normal(i), 0)); out.put(g.uv(i));
                    auto tangent = g.get(i, VertexSemantic::Tangent);
                    out.put(transformed(transform, tangent.xyz(), 0)); out.put(tangent.w);
                }
                out.count(g.indices.size());
                for (uint16_t i : g.indices) out.put(i);
            }
        }
        break;
    }
}

// A negative header identifies a list of convex leaves for moving bodies.
void hulls(Packet& out, const Bound* bound) {
    std::vector<std::vector<Vec3>> leaves;
    auto collect = [&](auto&& self, const Bound& shape, std::vector<Mat4>& stack) -> void {
        if (!shape.children.empty()) {
            for (auto& child : shape.children) {
                if (!child.bound) continue;
                stack.push_back(child.transform.matrix());
                self(self, *child.bound, stack);
                stack.pop_back();
            }
            return;
        }
        std::vector<Vec3> points;
        for (const auto& tri : bound_triangles(shape)) for (auto p : tri.v) {
            for (auto i = stack.rbegin(); i != stack.rend(); ++i) p = transformed(*i, p, 1);
            points.push_back(p);
        }
        if (points.size() >= 4) leaves.push_back(std::move(points));
    };
    std::vector<Mat4> stack;
    if (bound) collect(collect, *bound, stack);
    out.put(-static_cast<int32_t>(leaves.size()));
    for (auto& points : leaves) {
        out.count(points.size());
        for (auto& p : points) out.put(p);
    }
}

void model(Packet& out, const Drawable& d, const std::string& dictionary, bool movable) {
    meshes(out, d, dictionary, Mat4::identity());
    out.count(0);
    if (movable) hulls(out, d.bound.get()); else collision(out, d.bound.get());
}

Mat4 child_placement(const FragPhysicsLod& lod, size_t index) {
    auto m = index < lod.transforms.size() ? lod.transforms[index] : Mat4::identity();
    auto offset = lod.position_offset();
    m.at(3, 0) += offset.x;
    m.at(3, 1) += offset.y;
    m.at(3, 2) += offset.z;
    return m;
}

void placements(Packet& out, const std::vector<Entity>& entities) {
    out.count(entities.size());
    for (const auto& e : entities) {
        out.put(e.archetype_name);
        out.put(e.flags);
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
        out.put(a->flags);
        uint32_t doorType = 0;
        if ((a->flags & (1u << 26)) != 0) {
            auto definitions = read_ytyp(index.extract(index.files().at(file)));
            for (const auto& definition : definitions.archetypes)
                if (definition.name == member) { doorType = definition.special_attribute; break; }
        }
        out.put(doorType);
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
    if (kind == ModelFile || kind == DictionaryModel || kind == FragmentFile || kind == FragmentGroup) {
        auto parents = index.txd_parents(txd);
        out.count(parents.size() + 1);
        out.string(dictionary);
        for (auto parent : parents) out.string(index.name(parent));
    }
    bool movable = archetype && (archetype->flags & 0x20000) && !(archetype->flags & 0x20);
    switch (kind) {
    case Scene: {
        auto map = read_ymap(data);
        placements(out, map.entities);
        out.count(map.physics_dictionaries.size());
        for (auto hash : map.physics_dictionaries) out.string(index.name(hash));
        break;
    }
    case FragmentGroup: {
        auto f = read_yft(data);
        const auto& lod = f.physics.at(0).value();
        auto main = f.drawable ? &*f.drawable : nullptr;
        auto skeleton = main && main->skeleton ? &*main->skeleton : nullptr;
        auto bounds = lod.bound ? lod.bound.get() : lod.archetype ? lod.archetype->bound.get() : nullptr;
        std::vector<uint16_t> emitted;
        std::vector<BoundTriangle> triangles;
        for (size_t i = 0; i < lod.children.size(); ++i) {
            auto& c = lod.children[i];
            if (c.group_index != member) continue;
            auto bone = skeleton ? skeleton->find_tag(c.bone_tag) : nullptr;
            int b = bone ? static_cast<int>(bone - skeleton->bones.data()) : -1;
            if (c.drawable) meshes(out, *c.drawable, dictionary, child_placement(lod, i), -1, nullptr, main);
            if (bone && std::find(emitted.begin(), emitted.end(), c.bone_tag) == emitted.end()) {
                meshes(out, *main, dictionary, Mat4::identity(), b, &f);
                emitted.push_back(c.bone_tag);
            }
            if (!bounds || i >= bounds->children.size()) continue;
            const auto& shape = bounds->children[i];
            if (!shape.bound) continue;
            for (auto triangle : bound_triangles(*shape.bound)) {
                for (auto& v : triangle.v) v = transformed(shape.transform.matrix(), v, 1);
                triangles.push_back(triangle);
            }
        }
        out.count(0);
        out.count(triangles.size());
        for (auto& tri : triangles) for (auto& v : tri.v) out.put(v);
        out.put(lod.groups.at(member).mass());
        break;
    }
    case FragmentFile: {
        auto fragment = read_yft(data);
        auto main = fragment.drawable ? &*fragment.drawable : fragment.drawable_cloth ? &*fragment.drawable_cloth : nullptr;
        if (main) meshes(out, *main, dictionary, Mat4::identity(), -1, &fragment);
        if (fragment.physics[0]) {
            auto& lod = *fragment.physics[0];
            for (size_t i = 0; i < lod.children.size(); ++i)
                if (lod.children[i].drawable)
                    meshes(out, *lod.children[i].drawable, dictionary, child_placement(lod, i), -1, nullptr, main);
        }
        out.count(0);
        auto& lod = fragment.physics[0];
        auto bound = lod ? lod->bound.get() : nullptr;
        if (!bound && lod && lod->archetype) bound = lod->archetype->bound.get();
        if (!bound && fragment.drawable) bound = fragment.drawable->bound.get();
        if (movable) hulls(out, bound); else collision(out, bound);
        std::vector<uint32_t> groups;
        if (lod && index.archetype(index.files().at(file).name_hash))
            for (uint32_t i = 0; i < lod->groups.size(); ++i)
                if (detachable(fragment, i)) groups.push_back(i);
        out.count(groups.size());
        for (auto id : groups) {
            out.put(id); out.string(lod->groups[id].name()); out.put(lod->groups[id].strength());
        }
        out.put(lod && lod->archetype ? lod->archetype->mass() : 0.0f);
        break;
    }
    case ModelFile: model(out, read_ydr(data), dictionary, movable); break;
    case DictionaryModel: {
        auto dict = read_ydd(data);
        auto d = dict.find(member);
        if (!d) throw std::runtime_error("Missing drawable in dictionary");
        model(out, *d, dictionary, movable);
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
