#pragma once
#include "texture_io.h"
#include <miniz.h>
#include <algorithm>
#include <array>

// RSC7 stores the system segment before the GPU segment in one raw DEFLATE
// stream. Register dictionary members from the system segment alone.
class DictionaryReader {
    mz_stream stream{};
    rk::rsc::Resource resource;
    uint64_t graphicsSize = 0;

    void inflateInto(std::span<uint8_t> output) {
        while (!output.empty()) {
            auto chunk = static_cast<unsigned>(std::min<size_t>(output.size(), 1 << 20));
            stream.next_out = output.data();
            stream.avail_out = chunk;
            auto result = mz_inflate(&stream, MZ_NO_FLUSH);
            auto produced = chunk - stream.avail_out;
            if ((result != MZ_OK && result != MZ_STREAM_END) || !produced)
                throw rk::Error("Truncated or invalid dictionary");
            output = output.subspan(produced);
        }
    }

public:
    struct Member { uint32_t hash; uint64_t pointer; std::string name; };
    std::vector<Member> members;

    DictionaryReader(const DictionaryReader&) = delete;
    ~DictionaryReader() { mz_inflateEnd(&stream); }
    DictionaryReader(std::span<const uint8_t> bytes, bool textures) {
        using namespace rk;
        if (!rsc::Resource::is_resource(bytes) || bytes.size() - 16 > UINT32_MAX)
            throw Error("Invalid dictionary header");
        memcpy(&resource.version, bytes.data() + 4, 4);
        memcpy(&resource.system_flags.value, bytes.data() + 8, 4);
        memcpy(&resource.graphics_flags.value, bytes.data() + 12, 4);
        auto size = resource.system_flags.total_size();
        graphicsSize = resource.graphics_flags.total_size();
        if (size < 64 || size > rsc::Resource::kMaxSegmentSize) throw Error("Invalid dictionary metadata size");
        stream.next_in = bytes.data() + 16;
        stream.avail_in = static_cast<unsigned>(bytes.size() - 16);
        if (mz_inflateInit2(&stream, -MZ_DEFAULT_WINDOW_BITS) != MZ_OK) throw Error("Cannot initialize inflater");
        try {
            resource.system.resize(static_cast<size_t>(size));
            inflateInto(resource.system);
            Reader reader(resource);
            constexpr auto base = rsc::kSystemBase;
            auto hashes = reader.array<uint32_t>(reader.u64(base + 0x20), reader.u16(base + 0x28));
            auto pointers = reader.array<uint64_t>(reader.u64(base + 0x30), reader.u16(base + 0x38));
            if (hashes.size() != pointers.size()) throw Error("Dictionary member count mismatch");
            for (size_t i = 0; i < hashes.size(); ++i) {
                if (!pointers[i]) continue;
                members.push_back({hashes[i], pointers[i], textures ? reader.str(reader.u64(pointers[i] + 0x28)) : ""});
            }
        } catch (...) { mz_inflateEnd(&stream); throw; }
    }

    // The selected texture is the only GPU allocation. Some retail Enhanced
    // dictionaries exceed 256 MiB even though each texture fits by itself.
    rk::Texture texture(uint32_t hash) {
        using namespace rk;
        const auto it = std::find_if(members.begin(), members.end(), [hash](const Member& m){ return m.hash == hash; });
        if (it == members.end()) throw Error("Texture missing from dictionary");
        auto ptr = it->pointer;
        Reader reader(resource, rsc::game_of("ytd", resource.version));
        auto field = ptr + (reader.gen9() ? 0x38 : 0x70);
        auto address = reader.u64(field);
        if (address < rsc::kGraphicsBase) throw Error("Texture has no GPU payload");
        auto skip = address - rsc::kGraphicsBase;
        uint64_t size = reader.gen9()
            ? static_cast<uint64_t>(reader.u32(ptr + 8)) * reader.u32(ptr + 12)
            : texture_chain_size(static_cast<TextureFormat>(reader.u32(ptr + 0x58)), reader.u16(ptr + 0x50),
                                 reader.u16(ptr + 0x52), reader.u8(ptr + 0x5d), reader.u16(ptr + 0x54));
        if (skip > graphicsSize || size > graphicsSize - skip || size > INT32_MAX)
            throw Error("Texture exceeds GPU segment");
        std::array<uint8_t, 32768> discard;
        while (skip) {
            size_t n = static_cast<size_t>(std::min<uint64_t>(skip, discard.size()));
            inflateInto(std::span(discard).first(n));
            skip -= n;
        }
        resource.graphics.resize(static_cast<size_t>(size));
        inflateInto(resource.graphics);
        auto target = resource.system.data() + (field - rsc::kSystemBase);
        memcpy(target, &rsc::kGraphicsBase, sizeof(rsc::kGraphicsBase));
        return read_texture(reader, ptr);
    }
};
