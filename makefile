run:
	dotnet run --project src/BankingApp

health:
	curl -X GET http://localhost:5000/mcp/health

mcp:
	curl -X POST http://localhost:5000/mcp -H "Content-Type: application/json"

chat:
	curl -X POST http://localhost:5000/chat -H "Content-Type: application/json" -d '{"message": "What s Maria Chen s checking account balance?"}'

ms2:
	dotnet run --project eval/EvalRunner -- milestone2 --url http://localhost:5000 

ms3:
	dotnet run --project eval/EvalRunner -- milestone3 --url http://localhost:5000

docker-build:
	docker build -t bankingapp:latest .

docker-run:	
	docker run -p 8080:8080 --name bankingapp --rm bankingapp:latest