FROM ubuntu:22.04

RUN apt-get update && apt-get install -y openssh-server && \
    rm -rf /var/lib/apt/lists/*

RUN mkdir /var/run/sshd

# Create a non-root user 'dev' and set password from build arg
ARG SSH_USER=dev
ARG SSH_PASSWORD=devpass

RUN useradd -m -s /bin/bash $SSH_USER && \
    echo "$SSH_USER:$SSH_PASSWORD" | chpasswd && \
    mkdir -p /home/$SSH_USER/.ssh && \
    chown -R $SSH_USER:$SSH_USER /home/$SSH_USER/.ssh

# Allow password authentication (can be disabled later for key auth)
RUN sed -i 's/#PasswordAuthentication yes/PasswordAuthentication yes/' /etc/ssh/sshd_config

# Permit root login (can disable root login for better security)
RUN sed -i 's/#PermitRootLogin prohibit-password/PermitRootLogin no/' /etc/ssh/sshd_config

RUN sed -i 's/usePAM yes/usePAM no/' /etc/ssh/sshd_config

EXPOSE 22

CMD ["/usr/sbin/sshd", "-D"]
