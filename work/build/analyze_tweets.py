import csv, unicodedata, collections
p='work/corpus/TurkishTweets-data.txt'
errs=collections.Counter(); targets=collections.Counter(); categories=collections.Counter()
def lev(a,b):
 d=list(range(len(b)+1))
 for i,x in enumerate(a,1):
  nd=[i]+[0]*len(b)
  for j,y in enumerate(b,1): nd[j]=min(d[j]+1,nd[j-1]+1,d[j-1]+(x!=y))
  d=nd
 return d[-1]
with open(p,encoding='utf-8-sig') as f:
 for row in csv.reader(f,delimiter='\t'):
  if not row or len(row)<4 or row[0].lower().startswith('id'): continue
  inp,ref,ann=row[1].split(),row[2].split(),row[3].split()
  if not (len(inp)==len(ref)==len(ann)): continue
  for x,y,t in zip(inp,ref,ann):
   if len(x)<2 or len(y)<2 or not x.isalpha() or not y.isalpha() or 'OOV-ill_formed' not in t or x.lower()==y.lower(): continue
   x,y=x.lower(),y.lower(); errs[(x,y)]+=1; targets[y]+=1
   a=unicodedata.normalize('NFD',x); b=unicodedata.normalize('NFD',y)
   strip=lambda s: ''.join(c for c in s if unicodedata.category(c)!='Mn')
   dist=lev(x,y)
   if strip(a)==strip(b): categories['diacritic only']+=1
   elif dist==1: categories['one edit']+=1
   elif dist==2: categories['two edits']+=1
   else: categories['3+ edits']+=1
print('top pairs:')
for (x,y),n in errs.most_common(50): print(n, x,'->',y)
print('targets:')
for y,n in targets.most_common(30): print(n,y)
print('categories',categories)
