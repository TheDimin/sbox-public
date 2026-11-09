"""Independent-oracle controls; declared before authoring implementation."""
import importlib.util, math, unittest
from pathlib import Path
p=Path(__file__).with_name('author_reference.py')
spec=importlib.util.spec_from_file_location('negative_oracle',p)
o=importlib.util.module_from_spec(spec)

class NegativeScaleOracleTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        spec.loader.exec_module(o)
        cls.doc,cls.raw=o.load_source(o.SOURCE/'glTF/NegativeScaleTest.gltf')
        cls.ref=o.build_reference(cls.doc,cls.raw)

    def test_pinned_hierarchy_and_double_mirror_cancellation(self):
        self.assertEqual([r['node'] for r in self.ref['rows']],[0,1,2,3,4,5,6,8,9,11,12])
        self.assertEqual([r['node'] for r in self.ref['rows'] if r['determinant']<0],[4,6,8,11])
        self.assertGreater(o.det(o.worlds(self.doc)[9]),0)
        self.assertGreater(o.det(o.worlds(self.doc)[12]),0)

    def test_source_binary_accessors_and_world_values_agree_independently(self):
        doc,raw=o.load_source(o.SOURCE/'glTF-Binary/NegativeScaleTest.glb')
        r=o.build_reference(doc,raw)
        self.assertEqual(self.ref['rows'],r['rows'])
        self.assertEqual(self.ref['accessors'],r['accessors'])

    def test_source_bounds_basis_and_units_once(self):
        self.assertAlmostEqual(self.ref['bounds_min'][0],-19.685038196758963)
        self.assertAlmostEqual(self.ref['bounds_max'][1],203.21551270372285)
        self.assertAlmostEqual(self.ref['bounds_max'][2],175.33621450108808)

    def test_backface_materials_remain_single_sided(self):
        self.assertEqual([bool(m.get('doubleSided',False)) for m in self.doc['materials']],[False,False,False,True,True,True])

    def test_normals_are_inverse_transpose_and_reflections_keep_handedness(self):
        fixture,raw=o.fixture()
        ref=o.build_reference(fixture,raw)
        row=ref['rows'][0]
        n=ref['accessors'][1]['values'][0]
        world=o.worlds(fixture)[2]
        expected=o.normal(n,world)
        wrong=o.unit(o.direction(n,world))
        self.assertGreater(max(abs(a-b) for a,b in zip(expected,wrong)),.2)
        self.assertEqual(row['tangents'][0][3],-1)
        self.assertAlmostEqual(sum(a*b for a,b in zip(row['normals'][0],row['tangents'][0][:3])),0)

    def test_exact_singular_is_position_case_not_defined_normal(self):
        fixture,raw=o.fixture()
        for scale,rank in [([0,2,.75],2),([0,0,0],0)]:
            fixture['nodes'][2]['scale']=scale
            ref=o.build_reference(fixture,raw)
            row=ref['rows'][0]
            self.assertEqual(row['determinant'],0)
            self.assertIsNone(row['normals'])
            self.assertTrue(all(math.isfinite(x) for p in row['positions'] for x in p))
            if rank==0: self.assertEqual(row['positions'][0],row['positions'][1])
        fixture['nodes'][2]['scale']=[-1,2,.75]
        self.assertEqual(o.build_reference(fixture,raw)['rows'][0],o.build_reference(*o.fixture())['rows'][0])

    def test_tiny_anisotropic_invertible_normals_keep_reflection_sign(self):
        a=o.identity(); a[0][0]=-1e-30; a[1][1]=1e-30
        self.assertLess(o.det(a),0)
        n=o.normal(o.unit([1,1,1]),a)
        self.assertAlmostEqual(n[0],-1/math.sqrt(2))
        self.assertAlmostEqual(n[1],1/math.sqrt(2))
        self.assertGreater(n[2],0)
        # A float determinant underflow cannot authorize classifying this as singular.
        self.assertIsNotNone(n)

    def test_winding_and_parent_omission_negative_controls(self):
        row=next(r for r in self.ref['rows'] if r['node']==8)
        self.assertLess(row['determinant'],0)
        original=row['indices']; corrected=o.winding(original,row['determinant'])
        self.assertNotEqual(original,corrected)
        local=o.local(self.doc['nodes'][8])
        p=self.ref['accessors'][19]['values'][0]
        wrong=o.engine(o.point(p,local)); expected=row['positions'][0]
        self.assertGreater(max(abs(a-b) for a,b in zip(expected,wrong)),1)

if __name__=='__main__': unittest.main()
