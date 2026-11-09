import copy
import unittest
from visual_comparison_identity import comparison_identity,review_context
from group_visual_reviews import group_identity

class MaterialReviewContextTests(unittest.TestCase):
    def test_unselected_old_context_is_byte_equivalent(self):
        row={'id':'Existing/glTF','recipeApplied':{'complete':True},'capture':{'sampled':True}}
        self.assertEqual(review_context(row),{'animation':{},'complete':True,'limitations':None,'sampling':True})
    def test_distinct_expected_materials_do_not_share_review_even_with_hidden_visual_difference(self):
        a={'id':'Shoe/glTF','modelId':'Shoe','recipeApplied':{'materialVariant':{'sourceMaterialIndex':0}}};b=copy.deepcopy(a);b['recipeApplied']['materialVariant']['sourceMaterialIndex']=1
        self.assertNotEqual(group_identity(a,{'pixels':'same'},{'pixels':'reference'}),group_identity(b,{'pixels':'same'},{'pixels':'reference'}))
    def test_default_midnight_equivalent_selection_shares_group(self):
        a={'id':'Shoe/default','modelId':'Shoe','recipeApplied':{'materialVariant':{'sourceMaterialIndex':0,'authoredNames':['default','midnight']}}};b=copy.deepcopy(a);b['id']='Shoe/midnight'
        self.assertEqual(group_identity(a,{'pixels':'same'},{'pixels':'reference'}),group_identity(b,{'pixels':'same'},{'pixels':'reference'}))
if __name__=='__main__':unittest.main()
