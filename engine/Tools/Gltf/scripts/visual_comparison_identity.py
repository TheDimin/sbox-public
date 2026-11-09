"""Review identity: decoded frames/timing and observable review scope, never build metadata."""
import hashlib
import io
import json
from functools import lru_cache
from pathlib import Path
from PIL import Image


def digest(value):
    return hashlib.sha256(json.dumps(value, sort_keys=True, separators=(',', ':'), allow_nan=False).encode()).hexdigest()


@lru_cache(maxsize=128)
def decoded_media_bytes(body):
    with Image.open(io.BytesIO(body)) as image:
        frames = []
        for index in range(getattr(image, 'n_frames', 1)):
            image.seek(index)
            rgba = image.convert('RGBA')
            frames.append({'pixels': hashlib.sha256(rgba.tobytes()).hexdigest(), 'size': list(rgba.size),
                           'durationMs': round(float(image.info.get('duration', 0)), 6)})
        return {'frames': frames, 'loop': image.info.get('loop') if len(frames) > 1 else None}


def decoded_media(path):
    return decoded_media_bytes(Path(path).read_bytes())


def review_context(row):
    applied = row.get('recipeApplied') or {}
    animation = applied.get('animation') or {}
    context = {'animation': {key: animation[key] for key in ('clip', 'name', 'requestedClips', 'appliedClipCount',
            'requestedClipCount', 'mode', 'loop') if key in animation},
            'complete': applied.get('complete'), 'limitations': applied.get('limitations'),
            'sampling': (row.get('capture') or {}).get('sampled')}
    if applied.get('materialVariant') is not None:context['materialVariant']=applied['materialVariant']
    return context


def comparison_identity(row, capture=None, reference=None):
    # The case/form remains separate even if its pixels match another form.
    return digest({'version': 2, 'id': row['id'], 'capture': capture,
                   'reference': reference, 'context': review_context(row)})
