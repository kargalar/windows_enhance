import csv,gzip
allw=set();top100=set();top250=set()
with gzip.open('src/WinForge.Modules.Extras/Resources/TurkishNgramModel.tsv.gz','rt',encoding='utf-8') as f:
 for line in f:
  a=line.rstrip('\n').split('\t')
  if len(a)==3 and a[0]=='u':
   allw.add(a[1])
   if len(top100)<100000:top100.add(a[1])
   if len(top250)<250000:top250.add(a[1])
errors=0;hits=[0,0,0]
with open('work/corpus/TurkishTweets-data.txt',encoding='utf-8-sig') as f:
 for row in csv.reader(f,delimiter='\t'):
  if len(row)<4 or row[0].lower().startswith('id'):continue
  x,y,t=row[1].split(),row[2].split(),row[3].split()
  if len(x)!=len(y) or len(x)!=len(t):continue
  for a,b,c in zip(x,y,t):
   if 'OOV-ill_formed' not in c or a.lower()==b.lower() or not a.isalpha() or not b.isalpha():continue
   errors+=1
   for i,s in enumerate((top100,top250,allw)):
    if b.lower() in s:hits[i]+=1
print('errors',errors,'vocab target recall:',*[f'{n} ({n/errors:.2%})' for n in hits])

