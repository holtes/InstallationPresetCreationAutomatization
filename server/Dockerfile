FROM python:3.12-slim

WORKDIR /app

# System deps for Pillow, psycopg2, python-magic
RUN apt-get update && apt-get install -y --no-install-recommends \
    libpq-dev gcc libmagic1 \
    && rm -rf /var/lib/apt/lists/*

COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

COPY . .

# Create storage dirs
RUN mkdir -p /app/storage/assets /app/storage/builds

EXPOSE 8000

CMD ["uvicorn", "app.main:app", "--host", "0.0.0.0", "--port", "8000"]
