# CD Test Flatcar

A throwaway CCL flatcar for testing locomotive parts cargoes on a modded carrier.

It can carry all six vanilla parts and uses solid color blocks to identify them:

* TrainPartsDE2: red
* TrainPartsDE6: orange
* TrainPartsDH4: yellow
* TrainPartsDM3: green
* TrainPartsS060: blue
* TrainPartsS282A: magenta

## Editing

`UnityProject` requires CCL's CarCreator package, so before opening the project, import `CarCreator.unitypackage`
from the [CCL release](https://github.com/derail-valley-modding/custom-car-loader/releases).

The car lives in `Assets/_CCL_CARS/CD Test Flatcar`, the crates in its `Cargo/` folder. Export with the
**Export Pack** button on `CDTestFlatcar_pack.asset`, then copy the result back into `Mod/`.