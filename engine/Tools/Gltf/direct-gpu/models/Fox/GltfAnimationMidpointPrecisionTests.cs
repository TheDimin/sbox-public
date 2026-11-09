using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Sandbox.Tests.Gltf;

using Assert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

/// <summary>
/// A quaternion geodesic midpoint has an independent closed form: normalize
/// the sum of the unit endpoints after shortest-arc sign selection. This avoids
/// reproducing the sampler's trigonometric interpolation implementation.
/// Literals are FLOAT keys from pinned Khronos Fox: animation by tomkranis,
/// conversion by AsoboStudio/scurest (CC BY 4.0); PixelMannen model is CC0.
/// Nearest-float components are our precision target for these well-conditioned
/// fixtures, not a glTF requirement for bitwise agreement between implementations.
/// </summary>
[TestClass]
public sealed class GltfAnimationMidpointPrecisionTests
{
	private static readonly float[][] Cases =
	[
		[-.006675550248473883f, .13259024918079376f, -.4856732189655304f, .8640004396438599f,
		 .0020422544330358505f, .16477923095226288f, -.4823380708694458f, .8603450655937195f],
		[.0040152668952941895f, -.012611370533704758f, .30335259437561035f, .9527864456176758f,
		 .00113746989518404f, -.003572627669200301f, .30337703227996826f, .9528632164001465f],
		[-.013145077973604202f, -.006427699699997902f, .34547677636146545f, .9383132457733154f,
		 -.013160963542759418f, -.006396978162229061f, .3432823419570923f, .9391182661056519f],
		[.00030822824919596314f, .0011365815298631787f, -.39459556341171265f, .9188541769981384f,
		 .0006835304666310549f, .003005738602951169f, -.3690794110298157f, .9293927550315857f],
		[-.00523654418066144f, .0008093712385743856f, .3011893630027771f, .9535496234893799f,
		 -.016491856426000595f, .002979110460728407f, .2907942235469818f, .9566388130187988f],
		[-.006985324434936047f, -.002034666482359171f, .5284404158592224f, .8489392399787903f,
		 -.010640515014529228f, .00006866783951409161f, .2616761028766632f, .9650970697402954f],
		[1.7612626024288147e-8f, -4.083928750731047e-9f, -.2258821427822113f, .9741546511650085f,
		 1.7561310627911553e-8f, -4.299231193272135e-9f, -.2377905398607254f, .9713164567947388f],
		[3.2126216919066636e-15f, 7.335709649105657e-17f, .022828077897429466f, .999739408493042f,
		 3.206503149412172e-15f, 2.1131912963976254e-16f, .06576063483953476f, .9978353977203369f],
		[-.0013652672059834003f, .025750160217285156f, -.12176846712827682f, .9922235012054443f,
		 -.003814296331256628f, .023591043427586555f, -.0626712217926979f, .997748076915741f]
	];

	[TestMethod]
	[DataRow( 0 )]
	[DataRow( 1 )]
	[DataRow( 2 )]
	[DataRow( 3 )]
	[DataRow( 4 )]
	[DataRow( 5 )]
	[DataRow( 6 )]
	[DataRow( 7 )]
	[DataRow( 8 )]
	public void MidpointRetainsDoublePrecisionUntilTheFinalFloatRounding( int fixture )
	{
		var values = Cases[fixture];
		var a = Unit( values.Take( 4 ).Select( x => (double)x ).ToArray() );
		var b = Unit( values.Skip( 4 ).Select( x => (double)x ).ToArray() );
		var sign = a.Zip( b, (x, y) => x * y ).Sum() < 0 ? -1 : 1;
		var midpoint = Unit( a.Zip( b, (x, y) => x + sign * y ).ToArray() );
		var actual = Sandbox.Gltf.GltfAnimationSampler.Sample( [0, 2], values, 4, "LINEAR", 1, rotation: true );
		var actualUnit = Unit( actual.Select( x => (double)x ).ToArray() );
		var chord = Math.Sqrt( actualUnit.Zip( midpoint, (x, y) => (x - y) * (x - y) ).Sum() );
		var oppositeChord = Math.Sqrt( actualUnit.Zip( midpoint, (x, y) => (x + y) * (x + y) ).Sum() );
		var angleRadians = 4 * Math.Asin( Math.Clamp( Math.Min( chord, oppositeChord ) * .5, 0, 1 ) );
		for ( var component = 0; component < 4; component++ )
		{
			var expected = (float)midpoint[component];
			Assert.AreEqual( BitConverter.SingleToUInt32Bits( expected ), BitConverter.SingleToUInt32Bits( actual[component] ),
				$"Precision target fixture{fixture}/component{component}: actual={actual[component]:R}, analytic nearest float={expected:R}, orientation error={angleRadians:R}rad. This is a project accuracy target, not bitwise glTF conformance." );
		}
	}

	private static double[] Unit( double[] value )
	{
		var length = Math.Sqrt( value.Sum( x => x * x ) ); return value.Select( x => x / length ).ToArray();
	}
}
