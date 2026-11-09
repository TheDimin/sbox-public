import copy
import io
import json
from pathlib import Path
import unittest
from PIL import Image, PngImagePlugin
from visual_comparison_identity import decoded_media_bytes, comparison_identity


def image(color='red', note=None):
    stream=io.BytesIO();meta=PngImagePlugin.PngInfo()
    if note:meta.add_text('build',note)
    Image.new('RGBA',(3,3),color).save(stream,format='PNG',pnginfo=meta)
    return stream.getvalue()

def animation(colors=('red','blue'),durations=(100,100)):
    stream=io.BytesIO();frames=[Image.new('RGBA',(3,3),c) for c in colors]
    frames[0].save(stream,format='PNG',save_all=True,append_images=frames[1:],duration=durations,loop=0)
    return decoded_media_bytes(stream.getvalue())

class VisualIdentityTests(unittest.TestCase):
    def setUp(self): self.row={'id':'Test/glTF','capture':{'sampled':True},'recipeApplied':{'complete':True,'animation':{'clip':0}}}
    def identity(self,pixels,row=None):return comparison_identity(row or self.row,pixels,decoded_media_bytes(image()))
    def test_new_build_and_container_metadata_preserve_identity(self):
        row=copy.deepcopy(self.row);row.update(producer={'mvid':'new'},timestamp='tomorrow',sourceHash='new')
        self.assertEqual(self.identity(decoded_media_bytes(image())),self.identity(decoded_media_bytes(image(note='new build')),row))
    def test_changed_frame_invalidates_old_decision(self):self.assertNotEqual(self.identity(animation()),self.identity(animation(('red','green'))))
    def test_timing_change_requests_review(self):self.assertNotEqual(self.identity(animation()),self.identity(animation(durations=(100,200))))
    def test_scope_and_reference_changes_request_review(self):
        row=copy.deepcopy(self.row);row['recipeApplied']['animation']['clip']=1
        self.assertNotEqual(self.identity(animation()),self.identity(animation(),row))
        self.assertNotEqual(self.identity(animation()),comparison_identity(self.row,animation(),decoded_media_bytes(image('green'))))
    def test_main_includes_verified_owner_forms_and_all_media(self):
        root=Path(__file__).resolve().parents[4];dist=root/'Temp/gltf-validation/comparison-site/dist'
        data=json.loads((dist/'data.json').read_text());latest=json.loads((dist/'latest.json').read_text());cases={c['id']:c for c in data['cases']}
        isolated=[c for c in cases.values() if c.get('evidence',{}).get('type')=='isolated-model-execution']
        self.assertEqual(33,len(isolated));self.assertEqual(9,len({c['modelId'] for c in isolated}));self.assertEqual('mixed-individual-executions',data['evidenceCollection']['type'])
        self.assertEqual(sum(len(m['captures']) for m in latest['models']),len(isolated))
        import hashlib
        for row in cases.values():
            cap=row.get('capture')
            if cap:
                self.assertEqual(hashlib.sha256((dist/cap['url']).read_bytes()).hexdigest(),cap['sha256'])
                for u in [cap.get('posterUrl'),*cap.get('frameUrls',[])]:
                    if u:self.assertTrue((dist/u).is_file(),u)
            for u in row.get('reference',{}).get('frameUrls',[]):self.assertTrue((dist/u).is_file())
        for item in json.loads((dist/'review-queue.json').read_text())['queue']:
            self.assertEqual(cases[item['caseId']]['comparisonFingerprint'],item['comparisonFingerprint'])
        for decision in data['savedReviews']:self.assertEqual(cases[decision['caseId']]['comparisonFingerprint'],decision['fingerprint'])

if __name__=='__main__':unittest.main()
