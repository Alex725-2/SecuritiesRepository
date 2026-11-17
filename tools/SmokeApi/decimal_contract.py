import json,urllib.request,urllib.error
base='http://127.0.0.1:5088'
def call(path,body=None):
 req=urllib.request.Request(base+path,data=json.dumps(body).encode() if body is not None else None,headers={'Content-Type':'application/json',**({'Authorization':'Bearer '+token} if 'token' in globals() else {})})
 try:
  with urllib.request.urlopen(req) as r:return r.status,json.load(r)
 except urllib.error.HTTPError as e:return e.code,e.read().decode()
assert call('/api/health')[0]==503, 'This smoke test requires an unavailable test database.'
status,reply=call('/api/dev/token',{'role':'Operator'});assert status==200;token=reply['accessToken']
body={'idempotencyKey':'contract-smoke','sourceAccountId':'50000000-0000-0000-0000-000000000001','targetAccountId':'50000000-0000-0000-0000-000000000002','securityId':'40000000-0000-0000-0000-000000000001','quantity':80}
assert call('/api/operations/transfer',body)[0]==400
body['quantity']='80'
status,reply=call('/api/operations/transfer',body);assert status==503,(status,reply)
status,swagger=call('/swagger/v1/swagger.json');assert swagger['components']['schemas']['MovementRequest']['properties']['quantity']['type']=='string'
print('decimal numeric input:400; string input with unavailable PostgreSQL:503; Swagger decimal:string')
