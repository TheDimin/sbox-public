import copy
import unittest
from group_visual_reviews import group_identity

class SharedIdentityTests(unittest.TestCase):
    def setUp(self):
        self.a={'id':'Box/glTF','modelId':'Box','capture':{'sampled':True},'recipeApplied':{'complete':True}}
        self.frames={'frames':[{'pixels':'red','size':[2,2],'durationMs':100},{'pixels':'blue','size':[2,2],'durationMs':100}],'loop':0}
        self.reference={'frames':[{'pixels':'source','size':[2,2],'durationMs':0}],'loop':None}
    def identity(self,row=None,frames=None,reference=None):return group_identity(row or self.a,frames or self.frames,reference or self.reference)
    def test_exact_duplicate_source_and_compiled_share_one_review(self):
        b=copy.deepcopy(self.a);b.update(id='Box/glTF::compiled',variant='compiled',evidence={'receipt':'new','producer':'new-build'})
        self.assertEqual(self.identity(),self.identity(row=b))
    def test_changed_frame_timing_or_reference_does_not_share_review(self):
        b=copy.deepcopy(self.frames);b['frames'][1]['durationMs']=200
        self.assertNotEqual(self.identity(),self.identity(frames=b))
        b=copy.deepcopy(self.frames);b['frames'][1]['pixels']='green'
        self.assertNotEqual(self.identity(),self.identity(frames=b))
        self.assertNotEqual(self.identity(),self.identity(reference={'pixels':'different'}))
    def test_distinct_model_or_meaningful_scope_does_not_share_review(self):
        b=copy.deepcopy(self.a);b['modelId']='Other'
        self.assertNotEqual(self.identity(),self.identity(row=b))
        b=copy.deepcopy(self.a);b['recipeApplied']['complete']=False
        self.assertNotEqual(self.identity(),self.identity(row=b))

if __name__=='__main__':unittest.main()
