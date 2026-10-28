import csv,gzip
model='src/WinForge.Modules.Extras/Resources/TurkishNgramModel.tsv.gz'
words=set()
with gzip.open(model,'rt',encoding='utf-8') as f:
 for line in f:
  fields=line.rstrip('\n').split('\t')
  if len(fields)==3 and fields[0]=='u': words.add(fields[1])
errors=0; hits=0; miss=[]
with open('work/corpus/TurkishTweets-data.txt',encoding='utf-8-sig') as f:
 for row in csv.reader(f,delimiter='\t'):
  if len(row)<4 or row[0].lower().startswith('id'):continue
  a,b,t=row[1].split(),row[2].split(),row[3].split()
  if len(a)!=len(b) or len(a)!=len(t):continue
  for x,y,z in zip(a,b,t):
   if 'OOV-ill_formed' in z and x.lower()!=y.lower() and x.isalpha() and y.isalpha():
    errors+=1
    if y.lower() in words: hits+=1
    elif len(miss)<15: miss.append((x,y))
print(f'errors={errors} exact target in full unigram model={hits} ({hits/errors:.2%})')
print('sample absent targets:',miss)
print('vocabulary size:',len(words))
