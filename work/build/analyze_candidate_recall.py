import csv,gzip,math,collections
p='work/corpus/TurkishTweets-data.txt'; model='src/WinForge.Modules.Extras/Resources/TurkishNgramModel.tsv.gz'
allw=[]; top=set()
with gzip.open(model,'rt',encoding='utf-8') as f:
 for line in f:
  a=line.rstrip('\n').split('\t')
  if len(a)==3 and a[0]=='u':
   allw.append(a[1])
   if len(top)<100000: top.add(a[1])
coords={}
rows=['qwertyuıiopğü','asdfghjklş','zxcvbnmöç']
for r,row in enumerate(rows):
 for c,ch in enumerate(row): coords[ch]=(r+1,c+(0.3 if r==1 else 0.8 if r==2 else 0))
def kd(a,b):
 if a==b:return 0
 if (a,b) in [('c','ç'),('ç','c'),('g','ğ'),('ğ','g'),('i','ı'),('ı','i'),('o','ö'),('ö','o'),('s','ş'),('ş','s'),('u','ü'),('ü','u')]:return .25
 if a in coords and b in coords:
  x,y=coords[a];u,v=coords[b];return math.hypot(x-u,y-v)
 return 3
counts=collections.Counter(); errors=0; examples=collections.defaultdict(list)
with open(p,encoding='utf-8-sig') as f:
 for row in csv.reader(f,delimiter='\t'):
  if len(row)<4 or row[0].lower().startswith('id'):continue
  xs,ys,ts=row[1].split(),row[2].split(),row[3].split()
  if len(xs)!=len(ys) or len(xs)!=len(ts):continue
  for x,y,t in zip(xs,ys,ts):
   if 'OOV-ill_formed' not in t or x.lower()==y.lower() or not x.isalpha() or not y.isalpha():continue
   errors+=1;x=x.lower();y=y.lower(); counts['all']+=1
   if y in allw:counts['all vocab']+=1
   if y in top:counts['top100k']+=1
   if abs(len(x)-len(y))<=2:counts['len+-2']+=1
   comp=y[0]==x[0] or kd(x[0],y[0])<=1.8 or (len(y)>1 and y[1]==x[0]) or (len(x)>1 and y[0]==x[1])
   if comp:counts['compatible initial']+=1
   if y in top and abs(len(x)-len(y))<=2 and comp:counts['top+len+initial']+=1
   if y in allw and abs(len(x)-len(y))<=2 and comp:counts['all+len+initial']+=1
   if y in allw and abs(len(x)-len(y))<=2 and comp and y not in top and len(examples['long-tail'])<12: examples['long-tail'].append(f'{x}->{y}')
print('counts',dict(counts))
print('long-tail likely resolvable',examples['long-tail'])
