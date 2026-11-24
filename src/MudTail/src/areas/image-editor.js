// MudTail area 'image-editor': a large component the Blazor build loads on first use (blazor.js, load('image-editor')).

// Image editor: crop, rotate, adjust, draw, resize, output sizes, download and upload (+ the pure pipeline)
export { MudImageEditor } from '../image-editor/MudImageEditor.js';
export {
  applyAdjustments, histogram, fitSize, centeredCrop, rotatedBounds, presetAdjustments,
  DEFAULT_ADJUSTMENTS, FILTER_PRESETS, ASPECT_RATIOS, SIZE_PRESETS,
} from '../image-editor/pipeline.js';
export { loadSource as loadImage, renderSize, toBlob as canvasToBlob } from '../image-editor/render.js';
// Image recipes: store them with the original, regenerate the same files with renderRecipe(original, recipe)
export {
  createRecipe, normalizeRecipe, scaleEdit, matchSource, fingerprint, RecipeError, RECIPE_TYPE, RECIPE_VERSION,
} from '../image-editor/recipe.js';
export { renderRecipe, renderOutputs } from '../image-editor/outputs.js';
