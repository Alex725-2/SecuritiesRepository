import urllib.request, urllib.error, json
base='http://127.0.0.1:5088'
def call(path,body=None,token=None,expected=200):
 req=urllib.request.Request(base+path,data=None if body is None else json.dumps(body).encode(),headers={'Content-Type':'application/json',**({'Authorization':'Bearer '+token} if token else {})})
 try:
  with urllib.request.urlopen(req,timeout=10) as r:status=r.status;data=r.read()
 except urllib.error.HTTPError as e:status=e.code;data=e.read()
 assert status==expected,(path,status,data[:200]);print(path,status)
 return json.loads(data) if data else None
call('/swagger/v1/swagger.json')
call('/api/funds',expected=401)
token=call('/api/dev/token',{'role':'Auditor'})['accessToken']
call('/api/operations/transfer',{},token,403)
call('/api/dev/token',{'role':'Unknown'},expected=400)
call('/api/health',expected=503)
