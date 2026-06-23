// CPU fallback when chrome.offscreen is unavailable (Firefox dev build).

import "@tensorflow/tfjs-backend-cpu";
import * as tf from "@tensorflow/tfjs";
import { load as loadNsfwModel } from "nsfwjs/core";
import {
  MSG_CLASSIFY,
  MSG_CLASSIFY_BLOB,
  MSG_RESULT,
  DEFAULTS,
  nsfwScore,
  isBlockScore,
} from "./shared.js";

const api = typeof browser !== "undefined" ? browser : chrome;
const MODEL_INPUT = 224;
const THUMB_MAX_SIDE = 256;

let settings = { ...DEFAULTS };
let modelPromise = null;

async function getModel() {
  if (!modelPromise) {
    modelPromise = (async () => {
      await tf.setBackend("cpu");
      await tf.ready();
      return loadNsfwModel(api.runtime.getURL("models/model.json"), { size: MODEL_INPUT });
    })();
  }
  return modelPromise;
}

function isThumb(w, h) {
  return w > 0 && h > 0 && Math.max(w, h) <= THUMB_MAX_SIDE;
}

async function scoreBitmap(bitmap, isThumb) {
  const canvas = new OffscreenCanvas(MODEL_INPUT, MODEL_INPUT);
  const ctx = canvas.getContext("2d", { willReadFrequently: true });
  ctx.drawImage(bitmap, 0, 0, MODEL_INPUT, MODEL_INPUT);
  const image = ctx.getImageData(0, 0, MODEL_INPUT, MODEL_INPUT);
  const model = await getModel();
  const pixels = tf.browser.fromPixels(image);
  try {
    const predictions = await model.classify(pixels);
    const score = nsfwScore(predictions, settings.sexyWeight);
    return { block: isBlockScore(score, settings, isThumb, predictions), score, isThumb };
  } finally {
    pixels.dispose();
  }
}

export async function classifyCpu(msg) {
  const isThumbFlag =
    isThumb(msg.width || 0, msg.height || 0) ||
    (msg.type === MSG_CLASSIFY && /encrypted-tbn\d\.gstatic\.com/i.test(msg.url || ""));

  let blob;
  if (msg.type === MSG_CLASSIFY_BLOB) {
    blob = new Blob([msg.buffer], { type: msg.mime || "image/jpeg" });
  } else {
    const resp = await fetch(msg.url, { credentials: "omit", cache: "force-cache" });
    if (!resp.ok) throw new Error(`http ${resp.status}`);
    blob = await resp.blob();
  }

  const bitmap = await createImageBitmap(blob);
  try {
    const verdict = await scoreBitmap(bitmap, isThumbFlag);
    return { type: MSG_RESULT, id: msg.id, url: msg.url, verdict };
  } finally {
    bitmap.close?.();
  }
}
