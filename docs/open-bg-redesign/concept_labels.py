import json, collections
A=json.load(open('concept_map.json')); d=json.load(open('/mnt/c/OfflineDAoC/playable/runtime/server/open-bg-status.json'))
bots=d['Bots']; RN=("Albion","Midgard","Hibernia")
def common(L, page):
    L[A['status']]="Thidranki  |  on and ready  |  1 player inside"
    for i,realm in enumerate(RN):
        r=next(x for x in d['Realms'] if x['Realm']==realm); mine=[b for b in bots if b['Realm']==realm]
        inside=sum(1 for b in mine if b['State']=='inside' and b['Alive'])
        L[A['realm'][i][0]]=f"{realm}   {len(mine)} crew"
        L[A['realm'][i][1]]=f"8-man {r['Groups8Present']}/{r['Groups8']}  small {r['SmallPresent']}/{r['Small']}  solo {r['SolosPresent']}/{r['Solos']}"
        L[A['realm'][i][2]]="|"*55; L[A['realm'][i][3]]="|"*round(55*inside/max(1,len(mine)))
        L[A['bar_text'][i]]=f"{inside} in"
    L[A['banner']]="Keep 11: Albion holds it"
    L[A['phase']]="Quiet  |  next push in 35 min  |  field: 12 of 27 units out (72%)  |  world speed 1x"
    if page=="lage": L[A['tabs'][1]]="Situation"; L[A['tabs'][2]]="Setup"
    else: L[A['tabs'][0]]="Situation"; L[A['tabs'][3]]="Setup"
def row(L,r,slot,text):
    L[A['rowbase']+18*r+slot]=text
# ---- Lage
L={}; common(L,"lage")
for i,h in enumerate(("Unit / name","Lv","Class","Doing","Strength","Note")): L[A['heads'][i]]=h
rows=[]
open_units={('Albion',3),('Albion',32)}
for realm in RN:
    mine=[b for b in bots if b['Realm']==realm]; units=collections.OrderedDict()
    for b in sorted(mine,key=lambda b:({'group8':0,'small':1}.get(b['Kind'],2),b['Unit'])): units.setdefault(b['Unit'],[]).append(b)
    rows.append(('realm',realm,f"{len(units)} units, {len(mine)} crew",f"{sum(1 for b in mine if b['State']=='inside')} inside"))
    for u,members in units.items():
        b0=members[0]; title=(b0['Archetype'] or {'group8':'8-man','small':'small','solo':'solo'}[b0['Kind']])+f" #{u}"
        if b0['Kind']=='solo':
            rows.append(('solo',realm,b0)); continue
        opened=(realm,u) in open_units
        rows.append(('unit',realm,title,len(members),members,opened))
        if opened:
            for k,m in enumerate(members): rows.append(('member',realm,m,k==0))
L[A['page']]=f"Rows 1-18 of {len(rows)}"
for r,item in enumerate(rows[:18]):
    kind=item[0]
    if kind=='realm':
        row(L,r,3+RN.index(item[1]),item[1].upper()); row(L,r,8,item[2]); row(L,r,9,item[3])
    elif kind=='unit':
        _,realm,title,size,members,opened=item
        row(L,r,0,"-" if opened else "+"); row(L,r,1,title); row(L,r,8,f"{size} bots")
        role=(members[0]['Role'] or 'boarding').replace('_',' '); row(L,r,9,role)
        alive=sum(1 for m in members if m['Alive']); row(L,r,12,"="*size); row(L,r,13,"="*alive)
        row(L,r,15,"[Relieve]")
    elif kind=='member':
        _,realm,m,lead=item
        if lead: row(L,r,0,"*")
        row(L,r,3+RN.index(realm),m['Name']); row(L,r,6,str(m['Level'])); row(L,r,7,m['ClassName'])
        st='dead' if not m['Alive'] else (m['State'] if m['State']!='inside' else 'in the field')
        row(L,r,9 if st=='in the field' else (10 if st=='dead' else 11),st); row(L,r,13,f"{m['InsideSeconds']//60} min")
    elif kind=='solo':
        _,realm,m=item
        row(L,r,3+RN.index(realm),m['Name']); row(L,r,6,str(m['Level'])); row(L,r,7,m['ClassName'])
        row(L,r,8,"")
        row(L,r,9 if m['Alive'] else 10,(m['Role'] or '').replace('_',' ') if m['Alive'] else 'dead'); row(L,r,14,"solo")
L[A['hint']]="+ opens a unit, - folds it. * marks the leader. Click a crest to jump to its realm."
L[A['reply']]=""
json.dump({str(k):v for k,v in L.items()},open('concept_lage.json','w'))
# ---- Setup
L={}; common(L,"setup")
for i,h in enumerate(("Setting","","Range","Value","","")): L[A['heads'][i]]=h
L[A['heads'][0]]="Setting"; L[A['heads'][1]]=""; L[A['heads'][2]]=""
S=[('title',"Battleground"),
   ('switch',"Open BG","on or off",True,"[Turn off]"),
   ('choice',"Mode","who fights whom","Server PvP","[Use Per realm]"),
   ('choice',"Landing peace","who is safe at a landing","Auto","[Next: All]"),
   ('title',"Crew"),
   ('num',"Bot cap","8-120, step 4",72,8,120),
   ('num',"8-man per realm","0-3",1,0,3),
   ('num',"Small per realm","0-6",3,0,6),
   ('num',"Solos per realm","0-10",6,0,10),
   ('title',"Level band"),
   ('num',"Lowest level","1-50",44,1,50),
   ('num',"Highest level","44-50",50,1,50),
   ('title',"Group presets"),
   ('preset',"std8","8 bots, mixed",2),
   ('preset',"caster8","8 bots, casters",1),
   ('preset',"raid8","8 bots, raid lead",1),
   ('preset',"stealth4","4 stealthers",0),
   ('preset',"small","2-4 bots",2)]
L[A['page']]=f"Rows 1-18 of 24"
for r,item in enumerate(S):
    k=item[0]
    if k=='title': row(L,r,1,item[1])
    elif k=='switch':
        row(L,r,2,item[1]); row(L,r,8,item[2]); row(L,r,9 if item[3] else 11,"On" if item[3] else "Off"); row(L,r,15,item[4])
    elif k=='choice':
        row(L,r,2,item[1]); row(L,r,8,item[2]); row(L,r,9,item[3]); row(L,r,15,item[4])
    elif k=='num':
        _,name,rng,v,lo,hi=item; row(L,r,2,name); row(L,r,8,rng); row(L,r,9,str(v))
        row(L,r,12,"|"*40); row(L,r,13,"|"*round(40*(v-lo)/(hi-lo)))
        if v>lo: row(L,r,16,"[-]")
        if v<hi: row(L,r,17,"[+]")
    elif k=='preset':
        _,name,desc,v=item; row(L,r,2,name); row(L,r,8,desc); row(L,r,9 if v else 11,str(v)); row(L,r,12,"="*8); row(L,r,13,"="*v)
        if v>0: row(L,r,16,"[-]")
        row(L,r,17,"[+]")
row(L,5,0,">")
L[A['hint']]="Bot cap: the most crew bots in the open BG at once. The roster applies it within a game minute."
L[A['reply']]="Bot cap: 68 -> 72."
json.dump({str(k):v for k,v in L.items()},open('concept_setup.json','w'))
