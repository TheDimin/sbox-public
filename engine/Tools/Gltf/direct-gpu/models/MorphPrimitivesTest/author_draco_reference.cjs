#!/usr/bin/env node
// Separate integration of Google's pinned official glTF decoder. No production
// Sandbox.Gltf/Evergine code is invoked; shared codec ancestry remains explicit.
const fs = require('fs');
const path = require('path');
const crypto = require('crypto');
const hash = bytes => crypto.createHash('sha256').update(bytes).digest('hex');
async function main(checkoutRoot) {
  const checkout = path.resolve(checkoutRoot);
  const decoderRoot = path.join(checkout, 'Temp/gltf-validation/direct-gpu/CesiumMan/draco-1.5.7');
  const wrapperPath = path.join(decoderRoot, 'draco_wasm_wrapper_gltf.js');
  const wasmPath = path.join(decoderRoot, 'draco_decoder_gltf.wasm');
  const wrapper = fs.readFileSync(wrapperPath), wasm = fs.readFileSync(wasmPath);
  if (hash(wrapper) !== '8bb2952d2ba7d67e1414f8df819410cb0434a666be53f671fff75f68843d76f6' ||
      hash(wasm) !== '712db3449ae2041d6e8a224c395bda6cedb49e51322fae38b7db9beb8b381889') throw new Error('Official decoder identity changed.');
  const create = require(wrapperPath), module = await create({wasmBinary: wasm});
  const sourcePath = path.join(checkout, 'Temp/gltf-reference/glTF-Sample-Assets/Models/MorphPrimitivesTest/glTF-Draco/MorphPrimitivesTest.gltf');
  const source = fs.readFileSync(sourcePath), json = JSON.parse(source), bin = fs.readFileSync(path.join(path.dirname(sourcePath), json.buffers[0].uri));
  const primitives = [];
  for (let pi = 0; pi < json.meshes[0].primitives.length; pi++) {
    const primitive = json.meshes[0].primitives[pi], ext = primitive.extensions.KHR_draco_mesh_compression;
    const view = json.bufferViews[ext.bufferView], bytes = bin.subarray(view.byteOffset || 0, (view.byteOffset || 0) + view.byteLength);
    const decoder = new module.Decoder(), buffer = new module.DecoderBuffer(), mesh = new module.Mesh();
    try {
      buffer.Init(new Int8Array(bytes), bytes.length);
      const status = decoder.DecodeBufferToMesh(buffer, mesh);
      if (!status.ok() || !mesh.ptr) throw new Error('Official decode failed: ' + status.error_msg());
      const attributes = {};
      for (const [semantic, id] of Object.entries(ext.attributes)) {
        const accessor = json.accessors[primitive.attributes[semantic]], attribute = decoder.GetAttributeByUniqueId(mesh, id);
        if (accessor.componentType !== 5126 || accessor.normalized) throw new Error('Changed scoped FLOAT source requires review.');
        const values = new module.DracoFloat32Array();
        try {
          if (!decoder.GetAttributeFloatForAllPoints(mesh, attribute, values)) throw new Error('Attribute decode failed: ' + semantic);
          const raw = Buffer.alloc(values.size() * 4), numbers = [];
          for (let i = 0; i < values.size(); i++) { const v = values.GetValue(i); raw.writeFloatLE(v, i * 4); numbers.push(v); }
          attributes[semantic] = {componentType: 5126, components: attribute.num_components(), count: mesh.num_points(), normalized: false,
            rawSha256: hash(raw), rawBase64: raw.toString('base64'), values: numbers};
        } finally { module.destroy(values); }
      }
      const indices = [], triangle = new module.DracoInt32Array();
      try { for (let face = 0; face < mesh.num_faces(); face++) { decoder.GetFaceFromMesh(mesh, face, triangle); for (let c = 0; c < 3; c++) indices.push(triangle.GetValue(c)); } }
      finally { module.destroy(triangle); }
      primitives.push({primitive: pi, compressedSha256: hash(bytes), vertexCount: mesh.num_points(), indices, attributes});
    } finally { module.destroy(mesh); module.destroy(buffer); module.destroy(decoder); }
  }
  const result = {version: 1, pin: '90d7ede14c7e280af263824604b427a1ca02cb66', sourceSha256: hash(source), bufferSha256: hash(bin),
    decoder: {version: '1.5.7', commit: '8786740086a9f4d83f44aa83badfbea4dce7a1b5', license: 'Apache-2.0',
      jsSha256: hash(wrapper), wasmSha256: hash(wasm), independence: 'Separately integrated official WASM decoder; shared codec ancestry with production Draco. No Sandbox.Gltf/Evergine code invoked.'}, primitives};
  const out = path.join(__dirname, 'draco-reference.json'); fs.writeFileSync(out, JSON.stringify(result) + '\n');
  return {path: out, sha256: hash(fs.readFileSync(out)), primitives: primitives.length};
}
module.exports = {main};
if (require.main === module) main(process.argv[2]).then(result => process.stdout.write(JSON.stringify(result) + '\n')).catch(error => { process.stderr.write(error.stack + '\n'); process.exitCode = 1; });
