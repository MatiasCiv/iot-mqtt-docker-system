
FROM python:3.11-slim

WORKDIR /app

# Instalar dependencias del sistema necesarias para BLE
RUN apt-get update && apt-get install -y \
    bluetooth \
    bluez \
    libdbus-1-dev \
    && rm -rf /var/lib/apt/lists/*

# Copiar requirements primero (optimización de caché)
COPY requirements.txt .

# Instalar dependencias Python
RUN pip install --no-cache-dir -r requirements.txt

# Copiar el resto del código
COPY . .

CMD ["python", "main.py"]
