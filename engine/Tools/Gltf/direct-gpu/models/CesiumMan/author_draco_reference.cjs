#!/usr/bin/env node
"use strict";

// Test-input authoring only. This uses the pinned official JS/WASM integration,
// never Sandbox.Gltf/Evergine. Both integrations have shared codec ancestry;
// this is not an independently invented compression algorithm or GPU proof.
const fs = require("node:fs");
const path = require("node:path");
const crypto = require("node:crypto");
const childProcess = require("node:child_process");

const pin = "90d7ede14c7e280af263824604b427a1ca02cb66";
const commit = "8786740086a9f4d83f44aa83badfbea4dce7a1b5";
const expected = {
  "draco_wasm_wrapper_gltf.js": "8bb2952d2ba7d67e1414f8df819410cb0434a666be53f671fff75f68843d76f6",
  "draco_decoder_gltf.wasm": "712db3449ae2041d6e8a224c395bda6cedb49e51322fae38b7db9beb8b381889",
  "LICENSE": "d3709b0fb4b8a94bbb1d02b8a2e484f258b0d9c5c5a01f940391f3fe662cd1a4"
};

function requireCondition(value, message) {
  if (!value) throw new Error(message);
}
function hash(bytes) {
  return crypto.createHash("sha256").update(bytes).digest("hex");
}
function bounded(file, limit = 4 * 1024 * 1024) {
  requireCondition(fs.statSync(file).isFile() && fs.statSync(file).size <= limit, "Missing/oversized reference input: " + file);
  return fs.readFileSync(file);
}
function relativeResource(corpus, asset, uri) {
  requireCondition(typeof uri === "string" && !/^[a-zA-Z][a-zA-Z0-9+.-]*:/.test(uri) && !uri.startsWith("//") && !uri.includes("?") && !uri.includes("#"), "Reference resource must be local relative data.");
  const file = fs.realpathSync(path.resolve(path.dirname(asset), decodeURIComponent(uri)));
  requireCondition(file.startsWith(corpus + path.sep), "Reference resource escapes corpus.");
  return file;
}
function argumentsByName() {
  const result = {};
  for (let i = 2; i < process.argv.length; i += 2) {
    requireCondition(["--corpus", "--decoder-dir", "--output", "--source-relative"].includes(process.argv[i]) && process.argv[i + 1], "Expected --corpus, --decoder-dir, --output and optional --source-relative paths.");
    requireCondition(!(process.argv[i] in result), "Duplicate reference authoring argument.");
    result[process.argv[i]] = process.argv[i + 1];
  }
  requireCondition(["--corpus", "--decoder-dir", "--output"].every(name => name in result), "All three reference authoring paths are required.");
  return result;
}

async function main() {
  const args = argumentsByName();
  const corpus = fs.realpathSync(args["--corpus"]);
  const decoderDir = fs.realpathSync(args["--decoder-dir"]);
  const output = path.resolve(args["--output"]);
  const actualPin = childProcess.execFileSync("git", ["-C", corpus, "rev-parse", "HEAD"], {encoding: "utf8", maxBuffer: 4096}).trim();
  requireCondition(actualPin === pin, "Reference corpus revision changed.");
  const provenance = JSON.parse(bounded(path.join(decoderDir, "dependency.json")));
  requireCondition(provenance.commit === commit && provenance.tag === "1.5.7", "Decoder provenance is not the reviewed pinned version.");
  const decoderFiles = {};
  for (const [name, sha] of Object.entries(expected)) {
    const bytes = bounded(path.join(decoderDir, name));
    requireCondition(hash(bytes) === sha, "Official decoder file identity changed: " + name);
    decoderFiles[name] = bytes;
  }
  const relativeSource = args["--source-relative"] || "Models/CesiumMan/glTF-Draco/CesiumMan.gltf";
  requireCondition(!path.isAbsolute(relativeSource), "Reference source must be relative to the pinned corpus.");
  const sourcePath = fs.realpathSync(path.resolve(corpus, relativeSource));
  requireCondition(sourcePath.startsWith(corpus + path.sep), "Reference source escapes pinned corpus.");
  const sourceBytes = bounded(sourcePath);
  const document = JSON.parse(sourceBytes);
  const primitive = document.meshes[0].primitives[0];
  const extension = primitive.extensions.KHR_draco_mesh_compression;
  const view = document.bufferViews[extension.bufferView];
  const sourceBuffer = document.buffers[view.buffer];
  const bufferPath = relativeResource(corpus, sourcePath, sourceBuffer.uri);
  const buffer = bounded(bufferPath);
  requireCondition(buffer.length >= sourceBuffer.byteLength, "Truncated reference source buffer.");
  const viewOffset = view.byteOffset || 0;
  requireCondition(viewOffset >= 0 && view.byteLength > 0 && viewOffset + view.byteLength <= sourceBuffer.byteLength, "Compressed reference view escapes source buffer.");
  const compressed = buffer.subarray(viewOffset, viewOffset + view.byteLength);
  // Explicit wasmBinary prevents decoder runtime network fetches or system codec
  // fallbacks. The already hash-verified official JS is the only executed module.
  const createDecoder = require(path.join(decoderDir, "draco_wasm_wrapper_gltf.js"));
  const module = await createDecoder({wasmBinary: new Uint8Array(decoderFiles["draco_decoder_gltf.wasm"])});
  const decoder = new module.Decoder();
  const decoderBuffer = new module.DecoderBuffer();
  const mesh = new module.Mesh();
  let status = null;
  try {
    decoderBuffer.Init(new Int8Array(compressed), compressed.length);
    requireCondition(decoder.GetEncodedGeometryType(decoderBuffer) === module.TRIANGULAR_MESH, "Expected triangle mesh in selected Draco source.");
    status = decoder.DecodeBufferToMesh(decoderBuffer, mesh);
    requireCondition(status.ok() && mesh.ptr, "Official reference decoder failed: " + status.error_msg());
    const count = mesh.num_points();
    requireCondition(count > 0 && count < 100000 && mesh.num_faces() > 0 && mesh.num_faces() < 100000, "Reference decode exceeds bounded model scope.");
    const attributes = {};
    const formats = {
      [module.DT_INT8]: [5120, "INT8", "DracoInt8Array", "GetAttributeInt8ForAllPoints", "writeInt8", 1],
      [module.DT_UINT8]: [5121, "UINT8", "DracoUInt8Array", "GetAttributeUInt8ForAllPoints", "writeUInt8", 1],
      [module.DT_INT16]: [5122, "INT16", "DracoInt16Array", "GetAttributeInt16ForAllPoints", "writeInt16LE", 2],
      [module.DT_UINT16]: [5123, "UINT16", "DracoUInt16Array", "GetAttributeUInt16ForAllPoints", "writeUInt16LE", 2],
      [module.DT_UINT32]: [5125, "UINT32", "DracoUInt32Array", "GetAttributeUInt32ForAllPoints", "writeUInt32LE", 4],
      [module.DT_FLOAT32]: [5126, "FLOAT32", "DracoFloat32Array", "GetAttributeFloatForAllPoints", "writeFloatLE", 4]
    };
    for (const [semantic, id] of Object.entries(extension.attributes)) {
      const attribute = decoder.GetAttributeByUniqueId(mesh, id);
      requireCondition(attribute && attribute.ptr && attribute.unique_id() === id, "Missing decoder attribute id " + id);
      const format = formats[attribute.data_type()];
      requireCondition(format, "Unsupported actual decoded attribute data type: " + attribute.data_type());
      const [componentType, typeName, arrayClass, method, writer, width] = format;
      const accessor = document.accessors[primitive.attributes[semantic]];
      const components = attribute.num_components();
      requireCondition(components > 0 && components <= 16, "Unsupported decoded component count.");
      const data = new module[arrayClass]();
      try {
        requireCondition(decoder[method](mesh, attribute, data) && data.size() === count * components, "Decoded attribute extraction failed: " + semantic);
        const raw = Buffer.alloc(data.size() * width);
        const values = new Array(data.size());
        for (let i = 0; i < values.length; i++) {
          values[i] = data.GetValue(i);
          requireCondition(Number.isFinite(values[i]), "Nonfinite actual decoder attribute " + semantic + " at component " + i);
          raw[writer](values[i], i * width);
        }
        attributes[semantic] = {attribute_id: id, component_type: componentType, draco_type_name: typeName,
          draco_type_enum: attribute.data_type(), draco_normalized: attribute.normalized(),
          normalized: accessor.normalized || false, accessor_component_type: accessor.componentType,
          accessor_count: accessor.count, count, components, values, raw_base64: raw.toString("base64"), raw_sha256: hash(raw)};
      } finally {
        module.destroy(data);
      }
    }
    const indices = [];
    const face = new module.DracoInt32Array();
    try {
      for (let i = 0; i < mesh.num_faces(); i++) {
        requireCondition(decoder.GetFaceFromMesh(mesh, i, face) && face.size() === 3, "Invalid decoder triangle extraction.");
        for (let c = 0; c < 3; c++) {
          const index = face.GetValue(c);
          requireCondition(index >= 0 && index < count, "Decoded triangle index escapes geometry.");
          indices.push(index);
        }
      }
    } finally {
      module.destroy(face);
    }
    const weights = attributes.WEIGHTS_0.values;
    let minimum = Infinity, maximum = -Infinity, negative = 0, minSum = Infinity, maxSum = -Infinity;
    for (let i = 0; i < weights.length; i += 4) {
      let sum = 0;
      for (let c = 0; c < 4; c++) {
        const value = weights[i + c];
        minimum = Math.min(minimum, value); maximum = Math.max(maximum, value); negative += Number(value < 0); sum += value;
      }
      minSum = Math.min(minSum, sum); maxSum = Math.max(maxSum, sum);
    }
    const payload = {version: 1, pin, source: {relative: path.relative(corpus, sourcePath), sha256: hash(sourceBytes),
      buffer_relative: path.relative(corpus, bufferPath), buffer_sha256: hash(buffer), compressed_view: extension.bufferView, compressed_sha256: hash(compressed)},
      decoder: {version: "1.5.7", commit, license: "Apache-2.0", license_sha256: expected.LICENSE,
        js_sha256: expected["draco_wasm_wrapper_gltf.js"], wasm_sha256: expected["draco_decoder_gltf.wasm"],
        source_urls: provenance.files.map(file => file.url), independence: "Separate official JS/WASM integration, with shared codec ancestry to production native Draco; no Sandbox.Gltf/Evergine code used."},
      vertex_count: count, face_count: mesh.num_faces(), attributes, indices,
      weight_summary: {minimum, maximum, negative_components: negative, minimum_sum: minSum, maximum_sum: maxSum},
      author_sha256: hash(fs.readFileSync(__filename)),
      scope: "Test-input authoring only. Actual source-variant decoder output, unchanged weights/joint IDs/order; no engine or GPU pass claim."};
    fs.mkdirSync(path.dirname(output), {recursive: true});
    fs.writeFileSync(output, JSON.stringify(payload) + "\n");
    console.log(JSON.stringify({output, sha256: hash(fs.readFileSync(output)), vertex_count: count, face_count: mesh.num_faces(), weight_summary: payload.weight_summary, scope: payload.scope}));
  } finally {
    if (status) module.destroy(status);
    module.destroy(mesh); module.destroy(decoderBuffer); module.destroy(decoder);
  }
}

main().catch(error => { console.error(error.stack || error); process.exitCode = 1; });
