"""MetalRoughSpheres-only recipe contract; no visual or engine acceptance."""
import importlib.util
from pathlib import Path
import sys
import unittest

BASE = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(BASE / 'scripts'))
spec = importlib.util.spec_from_file_location('metalrough_recipe_generator', BASE / 'scripts' / 'build_reference_recipes.py')
recipes = importlib.util.module_from_spec(spec)
spec.loader.exec_module(recipes)


class MetalRoughEnvironmentRecipeTests(unittest.TestCase):
    def test_environment_and_light_are_scoped_to_spheres(self):
        env = recipes.environment_for('MetalRoughSpheres')
        self.assertEqual(env['type'], 'cubemap')
        self.assertEqual(env['sourceReferenceEnvironment'], 'unknown')
        self.assertEqual(env['sourceSha256'], 'ce074b5f3800017b20eea002544ef3030b05897f521d5142a403332fdc4cb0d6')
        self.assertEqual(env['compiledSha256'], '8273d4022f9f7ccdb43abce967c91d42f8519d6779492315510f635024947a19')
        self.assertEqual(env['tint'], [1, 1, 1, 1])
        self.assertEqual(recipes.directional_light_for('MetalRoughSpheres', [0, 0, 1])['intensity'], .5)
        self.assertEqual(recipes.directional_light_for('Duck', [0, 0, 1])['intensity'], 1)
        self.assertEqual(recipes.environment_for('Duck')['type'], 'engine-default')
        self.assertEqual(recipes.directional_light_for('Box', [0, 0, 1])['intensity'], 2)


if __name__ == '__main__':
    unittest.main()
