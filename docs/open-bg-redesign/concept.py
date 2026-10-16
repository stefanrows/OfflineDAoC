"""Concept 'Kriegstafel' mock: XML for Custom5 (scratch only) plus Lage/Setup sample labels."""
import json, collections
import xml.etree.ElementTree as ET
W, H = 760, 632
GOLD=(255,210,90); MUTED=(150,150,150); TEXT=(225,225,225); LINK=(240,200,110); STATUS=(200,200,200)
MESSAGE=(255,235,160); GREEN=(120,215,130); RED=(230,110,100); GREY=(120,120,120); DIM=(70,70,70)
REALM=((220,125,120),(135,165,235),(125,200,135))
root = ET.Element("Root_Element", ID="DAOCUi")
# own templates on stock textures (XML only)
t = ET.SubElement(root, "HorizontalResizeImageTemplate")
for k, v in (("Name","obg_banner"),("Height","24"),("LeftWidth","14"),("RepeatWidth","20"),("RightWidth","16")): ET.SubElement(t,k).text=v
tx = ET.SubElement(t,"Texture"); ET.SubElement(tx,"TextureName").text="frames_win"
for k,(x,y) in (("Left",(10,61)),("Repeat",(40,61)),("Right",(121,61))):
    p=ET.SubElement(tx,k); ET.SubElement(p,"X").text=str(x); ET.SubElement(p,"Y").text=str(y)
panel = ET.SubElement(root, "WindowTemplate")
for k,v in (("Name","custom5_window"),("WindowId","Custom5"),("Width",str(W)),("Height",str(H))): ET.SubElement(panel,k).text=v
def pos(e,x,y):
    p=ET.SubElement(e,"Position"); ET.SubElement(p,"X").text=str(x); ET.SubElement(p,"Y").text=str(y)
def full(x,y,w,h,tpl):
    e=ET.SubElement(panel,"FullResizeImageDef"); pos(e,x,y); ET.SubElement(e,"TemplateName").text=tpl; ET.SubElement(e,"Width").text=str(w); ET.SubElement(e,"Height").text=str(h)
def hr(x,y,w,tpl):
    e=ET.SubElement(panel,"HorizontalResizeImageDef"); pos(e,x,y); ET.SubElement(e,"TemplateName").text=tpl; ET.SubElement(e,"Width").text=str(w)
def area(x,y,tpl):
    e=ET.SubElement(panel,"ImageAreaDef"); pos(e,x,y); ET.SubElement(e,"TemplateName").text=tpl
def lab(x,y,w,col,adapter=None,text="",font="arial14",centered=False,end=False):
    e=ET.SubElement(panel,"LabelDef"); pos(e,x,y); c=ET.SubElement(e,"Color")
    for k,v in zip("RGBA",(*col,255)): ET.SubElement(c,k).text=str(v)
    ET.SubElement(e,"FontName").text=font; ET.SubElement(e,"Width").text=str(w); ET.SubElement(e,"Height").text="18"
    ET.SubElement(e,"Data").text=text; ET.SubElement(e,"TextCentered").text="true" if centered else "false"
    ET.SubElement(e,"EndAligned").text="true" if end else "false"
    if adapter is not None: ET.SubElement(e,"Adapter").text=f"cmgr_{adapter:03d}"
# ---- images
full(0,0,W,H,"dlg_sm_title_noresize")
full(8,24,W-16,100,"inner_frame")
CREST=("alb_symbol","mid_symbol","hib_symbol")
for i in range(3): area(16+246*i, 42, CREST[i])
hr(130,130,500,"obg_banner")
hr(8,178,W-16,"context_hr_image_divider")
for i,x in enumerate((12,118)): hr(x,184,100,"tab_button_normal")
hr(8,226,W-16,"context_hr_image_divider")
ROW_TOP, PITCH, ROWS = 232, 20, 18
for r in range(1,ROWS,2): hr(10, ROW_TOP+PITCH*r-1, W-20, "listbox_row_bg")
hr(8,ROW_TOP+PITCH*ROWS+2,W-16,"context_hr_image_divider")
# ---- labels
n=620
def nxt():
    global n; n+=1; return n-1
A={}
lab(12,1,80,GOLD,text="OPEN BG"); A['status']=nxt(); lab(92,1,W-140,STATUS,A['status'])
A['realm']=[]
for i in range(3):
    x=86+246*i
    a=[nxt() for _ in range(4)]; A['realm'].append(a)
    lab(x,40,160,REALM[i],a[0]); lab(x,60,160,MUTED,a[1]); lab(x,82,110,DIM,a[2]); lab(x,82,110,GREEN,a[3])
A['bar_text']=[nxt() for _ in range(3)]
for i in range(3): lab(86+246*i+114,82,44,MUTED,A['bar_text'][i])
A['banner']=nxt(); lab(130,130,500,(255,255,255),A['banner'],font="title",centered=True)
A['phase']=nxt(); lab(12,158,W-24,MUTED,A['phase'],centered=True)
A['tabs']=[nxt() for _ in range(4)]
for i,x in enumerate((12,118)):
    lab(x,184,100,MUTED,A['tabs'][2*i],centered=True); lab(x,184,100,GOLD,A['tabs'][2*i+1],centered=True)
A['page']=nxt(); lab(230,186,150,STATUS,A['page'])
for x,t_ in ((392,"[Open all]"),(466,"[Fold all]"),(540,"[Up]"),(576,"[Down]"),(628,"[Refresh]"),(694,"[Close]")): lab(x,186,80,LINK,text=t_)
heads=((28,"Unit / name"),(160,"Lv"),(190,"Class"),(292,"Doing"),(418,"Strength"),(504,"Note"))
A['heads']=[nxt() for _ in heads]
for (x,_),a in zip(heads,A['heads']): lab(x,208,120,MUTED,a)
SLOTS=((12,14,GOLD,True),(28,460,GOLD,False),(28,700,TEXT,False),(40,116,REALM[0],False),(40,116,REALM[1],False),(40,116,REALM[2],False),
       (160,24,TEXT,True),(190,98,TEXT,False),(160,128,MUTED,False),(292,122,GREEN,False),(292,122,RED,False),(292,122,GREY,False),
       (418,80,DIM,False),(418,80,GREEN,False),(504,140,GOLD,False),(650,96,LINK,False),(650,40,LINK,False),(700,40,LINK,False))
A['rowbase']=n
for r in range(ROWS):
    for s,(x,w,col,c) in enumerate(SLOTS):
        lab(x,ROW_TOP+PITCH*r,w,col,nxt(),centered=c)
A['hint']=nxt(); lab(12,ROW_TOP+PITCH*ROWS+8,W-24,MUTED,A['hint'])
A['reply']=nxt(); lab(12,ROW_TOP+PITCH*ROWS+26,W-24,MESSAGE,A['reply'])
print("labels used", n-620, "of 365; rows", ROWS, "slots", len(SLOTS))
ET.indent(root); open("concept.xml","wb").write(ET.tostring(root,encoding="iso-8859-1",xml_declaration=True))
json.dump(A, open("concept_map.json","w"))
