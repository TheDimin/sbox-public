// Emit only fields consumed by registered converters. Read offsets from the YTYP
// schema so different dictionary layout versions can share the same converter.
void archetype_extensions(const rk::GameIndex& index, uint32_t file, Packet& out) {
    using namespace rk;
    static const auto ladder = joaat("CExtensionDefLadder");
    auto meta = read_meta(index.extract(index.files().at(file)));
    if (!meta.find_block(ladder)) return;
    const auto* layout = meta.find_struct(ladder);
    if (!layout) layout = known_struct(ladder);
    if (!layout) throw Error("Missing ladder schema");
    auto offset = [&](const char* field) {
        auto hash = joaat(field);
        for (const auto& e : layout->entries)
            if (e.name == hash && e.type == metatype::Float_XYZ && e.offset >= 0)
                return static_cast<size_t>(e.offset);
        throw Error(std::string("Missing ladder vector: ") + field);
    };
    auto bottom = offset("bottom"), top = offset("top"), normal = offset("normal");
    for (const auto& a : map_types_from_meta(meta).archetypes) {
        for (const auto& ext : a.extensions) {
            if (ext.type != ladder) continue;
            out.put(a.name); out.put(ladder); out.count(3 * sizeof(Vec3));
            for (auto start : {bottom, top, normal}) {
                if (start + sizeof(Vec3) > ext.data.size()) throw Error("Truncated ladder vector");
                out.bytes.insert(out.bytes.end(), ext.data.begin() + start, ext.data.begin() + start + sizeof(Vec3));
            }
        }
    }
}
