# Quickstart

## Para montar un servidor local de seq sin autenticación

- `docker run --name seq -d --restart unless-stopped -e ACCEPT_EULA=Y -p 5341:80 datalust/seq:latest`
  - (o `docker compose up -d` gracias al archivo `compose.yaml` que tenemos ya configurado)

## Para enviar logs a Seq

Podemos ir a la carpeta con los logs, y usar este comando para que ingeste los logs del JSON:

- `seqcli ingest -i *.json --json`

> Info sobre `seqcli`:
> <https://docs.datalust.co/docs/command-line-client#ingest>

## Para ver la web de Seq

Accede a: <http://localhost:5341>

### Query para ver todos los medicamentos extraídos

`select distinct(Medications) from stream where Medications <> null`

# Enlaces sobre Seq

<https://hub.docker.com/r/datalust/seq>
<https://docs.datalust.co/docs/getting-started-with-docker>
<https://docs.datalust.co/docs/command-line-client>