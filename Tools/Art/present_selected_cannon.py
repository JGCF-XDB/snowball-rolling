"""Lay out selected B Blender views and actual Unity gameplay captures."""
import argparse
import shutil
import subprocess
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

parser=argparse.ArgumentParser()
parser.add_argument("project",type=Path)
parser.add_argument("--ffmpeg",type=Path)
args=parser.parse_args()
root=args.project.resolve()
out=root/"docs/art/selected-cannon-b"
smoke=root/"TestResults/SelectedCannonB/Smoke"
FONT="C:/Windows/Fonts/msyh.ttc"
BG="#102335"


def label(canvas,xy,text,size=28,color="#edf5fa"):
    ImageDraw.Draw(canvas).text(xy,text,font=ImageFont.truetype(FONT,size),fill=color)


def panel(canvas,path,box):
    image=Image.open(path).convert("RGB")
    image.thumbnail((box[2],box[3]),Image.Resampling.LANCZOS)
    canvas.paste(image,(box[0]+(box[2]-image.width)//2,box[1]+(box[3]-image.height)//2))


board=Image.new("RGB",(1800,1650),BG)
label(board,(35,20),"原 B 底座  /  暗色冰晶长管炮",46,"#ffc38c")
label(board,(38,87),"48 段圆周 · 三段倒角 · 连续曲面法线 · 炮口内径与炮弹外径均为 0.60 m",26)
panel(board,out/"B-hero.png",(35,145,1070,802))
label(board,(1150,140),"配套冰壳核心弹",28)
panel(board,out/"B-projectile.png",(1150,188,610,457))
label(board,(1150,684),"炮口正视 / 无遮挡的圆形内孔",28)
panel(board,out/"B-front.png",(1150,730,610,457))
for x,name,title in [(35,"side","侧视"),(610,"top","顶视"),(1185,"gameplay60","60° 俯角")]:
    label(board,(x,1220),title,27)
    panel(board,out/f"B-{name}.png",(x,1270,550,345))
board.save(out/"model-review.jpg",quality=95)

if (smoke/"cannon-projectile.png").exists():
    review=Image.new("RGB",(1920,1235),BG)
    label(review,(35,18),"暗色 B 已接入 Unity  /  实际游戏资源与动效",41,"#ffc38c")
    for i,(name,title) in enumerate([
        ("cannon-projectile.png","比赛镜头：暗色大炮、冰壳炮弹与拖尾"),
        ("selected-cannon-warning-close.png","示警：符文与警示线同步闪烁"),
        ("selected-cannon-launch-close.png","发射：炮口闪光与炮管后坐"),
        ("selected-cannon-impact-close.png","命中：冰晶与余烬消散")]):
        x=(i%2)*960; y=100+(i//2)*565
        label(review,(x+20,y),title,25)
        panel(review,smoke/name,(x,y+42,960,512))
    review.save(out/"gameplay-review.jpg",quality=95)
    shutil.copyfile(smoke/"cannon-projectile.png",out/"gameplay.png")
    for name in ["selected-cannon-warning-close.png","selected-cannon-launch-close.png","selected-cannon-impact-close.png"]:
        shutil.copyfile(smoke/name,out/name)

sequence=sorted((smoke/"motion").glob("*.png"))
if sequence:
    previews=[]
    for path in sequence:
        image=Image.open(path).convert("RGB").resize((768,432),Image.Resampling.LANCZOS)
        previews.append(image)
    # Centisecond durations alternate to approximate the source's 24 fps.
    durations=[40 if i%6 else 50 for i in range(len(previews))]
    palette_sheet=Image.new("RGB",(768,432*3))
    for i,index in enumerate([0,min(73,len(previews)-1),min(82,len(previews)-1)]):
        palette_sheet.paste(previews[index],(0,i*432))
    palette=palette_sheet.quantize(colors=256,method=Image.Quantize.MEDIANCUT)
    indexed=[frame.quantize(palette=palette,dither=Image.Dither.NONE) for frame in previews]
    indexed[0].save(out/"gameplay-motion.gif",save_all=True,append_images=indexed[1:],duration=durations,loop=0,optimize=True)
    if args.ffmpeg:
        subprocess.run([str(args.ffmpeg),"-y","-loglevel","error","-framerate","24","-i",str(smoke/"motion/%03d.png"),
                        "-c:v","libx264","-crf","18","-pix_fmt","yuv420p","-movflags","+faststart",str(out/"gameplay-motion.mp4")],check=True)
print("Selected B model and gameplay review saved to",out)
