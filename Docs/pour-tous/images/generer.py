"""
Génère les illustrations de la documentation « pour tous » avec le ComfyUI local (SDXL base, 1024 px, 25 pas),
puis les enregistre en JPEG 800 px à côté de ce script.

    python generer.py                 # toutes les images manquantes
    python generer.py 00 08           # seulement celles-ci (régénérées même si elles existent)
    python generer.py --graine 42 00  # avec une autre graine
    python generer.py --raffineur 00  # base + refiner : un peu plus fin, mais deux modèles en mémoire (machine bloquée)

ComfyUI doit tourner sur http://127.0.0.1:8188 (Launch_ComfyUI.bat).
"""
import json, os, subprocess, sys, time, urllib.parse, urllib.request

import cv2
import numpy as np

SERVEUR = "http://127.0.0.1:8188"
ICI = os.path.dirname(os.path.abspath(__file__))

STYLE = ("flat vector illustration, editorial style, soft pastel colors, simple clean shapes, gentle lighting, "
         "friendly, cozy, plain light background, high quality")
NEGATIF = ("text, letters, words, numbers, caption, watermark, signature, logo, blurry, lowres, jpeg artifacts, "
           "deformed, extra fingers, ugly, photo, photorealistic, 3d render, dark, scary")

# Une image par page : l'analogie de la page, en anglais pour SDXL.
IMAGES = {
    "00": "cross-section of a small restaurant building with exactly three floors stacked vertically: top floor dining room with customers at tables, middle floor kitchen with two cooks, bottom floor cellar storeroom with shelves of ingredients, one staircase linking the floors",
    "01": "top view of a tidy desk, one single laptop exactly in the center, a few neat cables going out from its side ports to a monitor, a mouse, a printer and a usb stick arranged in a circle around it, minimal and orderly",
    "02": "a big onion sliced in half on a cutting board, cross-section clearly showing many concentric rings, a tiny glowing golden gem in the very center of the rings",
    "03": "a town hall interior with several separate service counters side by side, each with its own clerk and a short queue of citizens holding forms",
    "04": "a judge in robes at a desk reading a file in a quiet office, and through the window a bailiff carrying documents walking outside",
    "05": "an open recipe book on a kitchen table, each page showing a complete dish with its ingredients laid out next to it",
    "06": "a cut-away office building with three separate departments side by side, each department with a front reception desk and a receptionist greeting a visitor, closed doors and filing cabinets behind each reception",
    "07": "a shopping mall interior under one big glass roof, several different small shops side by side each with its own shop window and cashier",
    "08": "a small village of separate little shops, each in its own small house, a delivery person on a bicycle carrying parcels between them",
    "09": "split scene: left half an open kitchen with two chefs cooking, right half a dining room where customers look at a big board on the wall covered with small colorful drawings of dishes, no writing",
    "10": "an open old-fashioned ledger notebook with many handwritten lines, a fountain pen and a cup of coffee on a wooden desk",
    "11": "an architect sitting with a family of villagers around a table, listening carefully and taking notes, house sketches on the table",
    "12": "a taxi car picking up a passenger with a suitcase in a city street, a parked private car covered with a tarp in a garage nearby",
    "13": "one single cordless power drill lying in the center of a clean workbench, an open case next to it holding a neat row of interchangeable drill bits and screwdriver tips, minimal composition",
    "14": "a wedding planner woman standing in the middle of a garden wedding venue with a clipboard, pointing and directing a florist carrying flowers, a baker carrying a wedding cake and a photographer, all moving toward her",
    "15": "a cut-away apartment building with many apartments, each apartment with different furniture and a different family, each door with its own key",
    "16": "two friendly butlers serving two different guests, one guest with a large desktop computer screen and one with a smartphone, each butler bringing a different tray",
    "17": "a row of five identical small glass greenhouses side by side in a garden, four of them full of healthy green plants, one in the middle with a broken glass pane, frost inside and wilted plants",
    "18": "a row of post office clerks, each one at their own desk with their own mailbox full of letters, each calmly handling one letter at a time",
    "19": "a busy supermarket with many checkout lanes open, cashiers serving queues of shoppers, staff opening an extra checkout lane",
    "20": "a car assembly line in a bright factory, robots and workers at successive stations along a conveyor belt, each adding one part",
    "21": "a group of dancers performing together on a stage without any conductor, each one reacting to the movement of the others",
    "22": "a house being renovated room by room while a family keeps living in it, one room finished and modern, another room with scaffolding and workers",
    "23": "three ways of ordering food: a person reading a restaurant menu with a waiter, a person filling a structured order form, a person ticking items on a checklist",
    "24": "split illustration: on the left a person mailing a letter at a mailbox and receiving a reply letter, on the right a car dashboard with live moving gauges",
    "accueil": "a friendly city made of many different buildings seen from above, houses, shops, a factory, a town hall, connected by roads, like a toy town",
}


# Graines retenues après essais (les autres images utilisent la graine par défaut). Les images 00 à 17 de la première
# série ont été faites avec le refiner ; refaites aujourd'hui sans lui, elles seraient proches mais pas identiques.
GRAINES = {"00": 22, "09": 11, "17": 11}


def requete(chemin, donnees=None):
    if donnees is not None:
        donnees = json.dumps(donnees).encode("utf-8")
    req = urllib.request.Request(SERVEUR + chemin, data=donnees, headers={"Content-Type": "application/json"})
    with urllib.request.urlopen(req, timeout=60) as reponse:
        return reponse.read()


def workflow(prompt, graine, raffineur):
    positif = f"{prompt}, {STYLE}"
    if not raffineur:
        # Un seul modèle en mémoire : bien plus léger pour la machine, et suffisant pour des illustrations en aplats.
        return {
            "1": {"class_type": "CheckpointLoaderSimple", "inputs": {"ckpt_name": "sd_xl_base_1.0.safetensors"}},
            "3": {"class_type": "CLIPTextEncode", "inputs": {"clip": ["1", 1], "text": positif}},
            "4": {"class_type": "CLIPTextEncode", "inputs": {"clip": ["1", 1], "text": NEGATIF}},
            "7": {"class_type": "EmptyLatentImage", "inputs": {"width": 1024, "height": 1024, "batch_size": 1}},
            "8": {"class_type": "KSampler", "inputs": {
                "model": ["1", 0], "positive": ["3", 0], "negative": ["4", 0], "latent_image": ["7", 0],
                "seed": graine, "steps": 25, "cfg": 7.0, "sampler_name": "dpmpp_2m", "scheduler": "karras", "denoise": 1.0}},
            "10": {"class_type": "VAEDecode", "inputs": {"samples": ["8", 0], "vae": ["1", 2]}},
            "11": {"class_type": "SaveImage", "inputs": {"images": ["10", 0], "filename_prefix": "ArchiPourTous"}},
        }
    return {
        "1": {"class_type": "CheckpointLoaderSimple", "inputs": {"ckpt_name": "sd_xl_base_1.0.safetensors"}},
        "2": {"class_type": "CheckpointLoaderSimple", "inputs": {"ckpt_name": "sd_xl_refiner_1.0.safetensors"}},
        "3": {"class_type": "CLIPTextEncode", "inputs": {"clip": ["1", 1], "text": positif}},
        "4": {"class_type": "CLIPTextEncode", "inputs": {"clip": ["1", 1], "text": NEGATIF}},
        "5": {"class_type": "CLIPTextEncodeSDXLRefiner", "inputs": {"clip": ["2", 1], "text": positif, "ascore": 6.0, "width": 1024, "height": 1024}},
        "6": {"class_type": "CLIPTextEncodeSDXLRefiner", "inputs": {"clip": ["2", 1], "text": NEGATIF, "ascore": 2.5, "width": 1024, "height": 1024}},
        "7": {"class_type": "EmptyLatentImage", "inputs": {"width": 1024, "height": 1024, "batch_size": 1}},
        "8": {"class_type": "KSamplerAdvanced", "inputs": {
            "model": ["1", 0], "positive": ["3", 0], "negative": ["4", 0], "latent_image": ["7", 0],
            "add_noise": "enable", "noise_seed": graine, "steps": 25, "cfg": 7.0, "sampler_name": "dpmpp_2m",
            "scheduler": "karras", "start_at_step": 0, "end_at_step": 20, "return_with_leftover_noise": "enable"}},
        "9": {"class_type": "KSamplerAdvanced", "inputs": {
            "model": ["2", 0], "positive": ["5", 0], "negative": ["6", 0], "latent_image": ["8", 0],
            "add_noise": "disable", "noise_seed": graine, "steps": 25, "cfg": 7.0, "sampler_name": "dpmpp_2m",
            "scheduler": "karras", "start_at_step": 20, "end_at_step": 25, "return_with_leftover_noise": "disable"}},
        "10": {"class_type": "VAEDecode", "inputs": {"samples": ["9", 0], "vae": ["1", 2]}},
        "11": {"class_type": "SaveImage", "inputs": {"images": ["10", 0], "filename_prefix": "ArchiPourTous"}},
    }


def baisser_priorite():
    """ComfyUI en priorité basse : l'utilisateur garde la main sur sa machine pendant la série (Windows seulement)."""
    if os.name != "nt":
        return
    script = ("Get-CimInstance Win32_Process | Where-Object { $_.Name -eq 'python.exe' -and $_.CommandLine -like '*main.py*' } "
              "| ForEach-Object { (Get-Process -Id $_.ProcessId).PriorityClass = 'BelowNormal' }")
    subprocess.run(["powershell", "-NoProfile", "-Command", script], check=False)


def generer(cle, graine, raffineur):
    debut = time.time()
    identifiant = json.loads(requete("/prompt", {"prompt": workflow(IMAGES[cle], graine, raffineur)}))["prompt_id"]
    while True:
        time.sleep(2)
        historique = json.loads(requete(f"/history/{identifiant}"))
        if identifiant in historique:
            entree = historique[identifiant]
            if entree.get("status", {}).get("status_str") == "error":
                raise RuntimeError(f"{cle} : échec de ComfyUI")
            if entree.get("outputs"):
                break
    image = entree["outputs"]["11"]["images"][0]
    parametres = urllib.parse.urlencode({"filename": image["filename"], "subfolder": image["subfolder"], "type": image["type"]})
    octets = requete(f"/view?{parametres}")
    pixels = cv2.imdecode(np.frombuffer(octets, np.uint8), cv2.IMREAD_COLOR)
    pixels = cv2.resize(pixels, (800, 800), interpolation=cv2.INTER_AREA)
    sortie = os.path.join(ICI, f"{cle}.jpg")
    cv2.imwrite(sortie, pixels, [cv2.IMWRITE_JPEG_QUALITY, 85])
    print(f"{cle}.jpg  graine {graine}  {time.time() - debut:.0f} s  {os.path.getsize(sortie) // 1024} Ko", flush=True)


if __name__ == "__main__":
    arguments = sys.argv[1:]
    graine = None
    if "--graine" in arguments:
        i = arguments.index("--graine")
        graine = int(arguments[i + 1])
        del arguments[i:i + 2]
    raffineur = "--raffineur" in arguments
    if raffineur:
        arguments.remove("--raffineur")
    cles = arguments or [c for c in IMAGES if not os.path.exists(os.path.join(ICI, f"{c}.jpg"))]
    baisser_priorite()
    for cle in cles:
        generer(cle, graine if graine is not None else GRAINES.get(cle, 20261011), raffineur)
