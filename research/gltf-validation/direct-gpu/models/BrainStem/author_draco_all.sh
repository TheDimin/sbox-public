#!/bin/bash
set -eu
corpus=$(realpath "$1")
decoder=$(realpath "$2")
out=$(realpath "$3")
node_bin=$(realpath "$4")
actual_pin=$(git -C "$corpus" rev-parse HEAD)
test "$actual_pin" = 90d7ede14c7e280af263824604b427a1ca02cb66
for i in $(seq 0 58); do
 BRAINSTEM_CORPUS_PIN="$actual_pin" "$node_bin" "$(dirname "$0")/author_draco_primitive.cjs" --corpus "$corpus" --decoder-dir "$decoder" --source-relative Models/BrainStem/glTF-Draco/BrainStem.gltf --primitive "$i" --output "$out/draco-$(printf '%02d' "$i").json"
done
python3 "$(dirname "$0")/author_draco_pose.py" "$corpus" "$out" "$out/draco-reference.json"
