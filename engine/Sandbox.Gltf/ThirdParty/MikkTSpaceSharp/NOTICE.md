# MikkTSpaceSharp source

This directory vendors the managed port by Roman Shapiro at revision
`9b8d5c683a763daa9fffd81400f6ff8e81adf5bf` from
https://github.com/rds1983/MikkTSpaceSharp. It is a generated C# port of
Morten S. Mikkelsen's MikkTSpace. The port license is reproduced in `LICENSE.txt`.
This is not the older unsafe NuGet 0.5.3 package.

Alterations: the namespace and top-level visibility are isolated, and
`AssignRecur`, `MergeVertsFast`, `QuickSort` and `QuickSortEdges` use explicit
left-first work stacks instead of recursive calls.
All other algorithm code is the pinned managed port. These changes have not yet
been verified against the original C implementation.

Original MikkTSpace notice from https://github.com/mmikk/MikkTSpace:

Copyright (C) 2011 by Morten S. Mikkelsen

This software is provided 'as-is', without any express or implied
warranty. In no event will the authors be held liable for any damages
arising from the use of this software.

Permission is granted to anyone to use this software for any purpose,
including commercial applications, and to alter it and redistribute it
freely, subject to the following restrictions:

1. The origin of this software must not be misrepresented; you must not
   claim that you wrote the original software. If you use this software
   in a product, an acknowledgment in the product documentation would be
   appreciated but is not required.
2. Altered source versions must be plainly marked as such, and must not be
   misrepresented as being the original software.
3. This notice may not be removed or altered from any source distribution.
