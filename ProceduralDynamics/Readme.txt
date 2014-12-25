Procedural Dynamics Procedural Wings
by DYJ

==Introduction
The procedural dynamics procedural wing (pWing for short) is a wing piece that the user can procedurally manipulate, the wing automatically generates colliders and set its .cfg parameters accordingly.

==Instructions / key commands:
Once a wing-section is placed mouse movement + the following commands changes the shape of the wing.

Root scale : Mouseover + B
Tip scale : Mouseover + T
Tip position : Mouseover + G

Debug print: O


==Autosnapping:
When a section is attached to another it snaps into place on its parent part to allow for "seamless" joints, this doesn't affect rotation so you can use WASDQE to angle stuff like normal.
Autosnapping can be disabled by holding down the right mousebutton as the part is attached.

==Changelog:
0.1 - "Alpha" Release
0.2:
- (sort of)Added reflective FAR support
- Code refactoring by Taverius
- Unborked a bunch of stuff
- Further optimization
- Mirrored parts now automatically get updated
- Lift indicator should be correct.
0.3:
- Proper (afaik) FAR support
- Unborking of the attach override logic.
0.4:
-Added B9 aerospace wing, asset by Bac9.
0.5:
-Added adapter thing, updated references, might have fixed the CoL indicator some people were having with vanilla ksp.
0.6:
-Added 2 controlsurfaces and one 1 new wing part, Added WIP thickness scaling.
0.7:
-Fixed name conflict,Added allmoving controlsurface, changed to nonrelative thickness scaling, changing thickness now autoupdates. Might have fixed something else too that I don't remember.
0.8:
- Changed internal math from floats to doubles to keep up with Ferrams changes to FAR, didn't fix any of the bugs you expected me to. Also, added another wing type that's kind of WIP.
0.8.1:
-Added basic cost support, change it by adjusting "costDensity", default is 2000.
0.9:
-Taverius update! Added SP+ wing (thanks Porkjet) Fixed tons of bugs, added an option to revert to relative thickness scaling among other things also NEAR support, check bitbucket for a full changelog.
0.9.1:
-NathanKell update! Added compatibility with latest FAR
0.9.2:
-Added compatibility for KSP 0.90 (thanks to taniwha, MattHP85, ckfinite).
-Change R key to B (per abrr2000) since KSP 0.90 uses R in the editor already.
-Fixed some NREs for debris (thanks to taniwha).
0.9.3
-Fixed for NEAR.
-Resave PNGs so the DDS converter can read them.

==Install:
pWings work via the GameData system, so drop the ProceduralDynamics folder in there.

==Known issues / Improvable areas / FAQ / Words / What does this mean?!:
Editing a mirrored part or parts that are in weird orientations results in unintuitive controls, can usually be fixed by moving the camera to the other side.

Reflective support? Reflective FAR/NEAR support means that to install the FAR/NEAR version you simply make sure that FAR is installed.

Elliptical wings don't have their own math yet, and as such they behave like trapezoidal ones.

The zero clamp intended to prevent you from inverting your wing doesn't really work and instead makes other möbius shapes.

There is a SP+ style wing but no controlsurface, What gives?! That's coming


How do I make my own?
Example files:
https://dl.dropboxusercontent.com/u/...%20Example.zip

==License
Do not distribute derivatives without my permission, feel free to tinker with it but ask me before you release anything. To clarify this applies to the plugin, Assets you can do whatever you want with.