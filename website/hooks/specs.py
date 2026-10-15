import os
import shutil


def on_post_build(config):
    """The starter specs of templates/specs go out as downloads next to the gallery script (assets/specs/*.json)."""
    source = os.path.join(os.path.dirname(config["config_file_path"]), "..", "templates", "specs")
    target = os.path.join(config["site_dir"], "assets", "specs")
    os.makedirs(target, exist_ok=True)
    for name in os.listdir(source):
        if name.endswith(".json"):
            shutil.copyfile(os.path.join(source, name), os.path.join(target, name))
