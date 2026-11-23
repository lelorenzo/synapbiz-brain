-- The tables of the process host. Applied once, by the migrator, with the search path set to the configured schema.
-- Every table has the tenant as the first column of its primary key. Times are timestamptz and come from the application clock.

create table process_instance (
    tenant_id text not null,
    instance_id text not null,
    process_id text not null,
    process_version bigint not null,
    process_hash text not null,
    business_key text not null,
    status smallint not null,
    stage text null,
    current_step text null,
    result text null,
    problem_code text null,
    state text null,
    revision bigint not null,
    parent_instance_id text null,
    parent_child_key text null,
    created_at timestamptz not null,
    updated_at timestamptz not null,
    constraint pk_process_instance primary key (tenant_id, instance_id),
    constraint uq_process_instance_key unique (tenant_id, process_id, business_key)
);

create index ix_process_instance_parent on process_instance (tenant_id, parent_instance_id) where parent_instance_id is not null;

create table process_history (
    tenant_id text not null,
    instance_id text not null,
    sequence bigint not null,
    at timestamptz not null,
    step text null,
    entry bigint not null,
    attempt int not null,
    kind smallint not null,
    code text null,
    data text null,
    constraint pk_process_history primary key (tenant_id, instance_id, sequence)
);

create table process_timer (
    tenant_id text not null,
    timer_id text not null,
    instance_id text not null,
    due_at timestamptz not null,
    constraint pk_process_timer primary key (tenant_id, timer_id)
);

create index ix_process_timer_due on process_timer (due_at);
create index ix_process_timer_instance on process_timer (tenant_id, instance_id);

create table process_wait (
    tenant_id text not null,
    wait_id text not null,
    instance_id text not null,
    event text not null,
    correlation text null,
    constraint pk_process_wait primary key (tenant_id, wait_id)
);

create index ix_process_wait_event on process_wait (tenant_id, event, correlation);
create index ix_process_wait_instance on process_wait (tenant_id, instance_id);

create table process_task (
    tenant_id text not null,
    task_id text not null,
    instance_id text not null,
    step text not null,
    queue text not null,
    outcomes text not null,
    show text not null,
    status smallint not null,
    created_at timestamptz not null,
    closed_at timestamptz null,
    constraint pk_process_task primary key (tenant_id, task_id)
);

create index ix_process_task_queue on process_task (tenant_id, queue, status);
create index ix_process_task_instance on process_task (tenant_id, instance_id);

create table process_effect (
    tenant_id text not null,
    effect_id text not null,
    instance_id text not null,
    step text not null,
    idempotency_key text not null,
    kind smallint not null,
    port text not null,
    input text not null,
    attempt int not null,
    delivery smallint not null,
    model text null,
    prompt_id text null,
    prompt_version bigint null,
    status smallint not null,
    due_at timestamptz not null,
    lease_until timestamptz null,
    lease_owner text null,
    constraint pk_process_effect primary key (tenant_id, effect_id)
);

create index ix_process_effect_status_due on process_effect (status, due_at);
create index ix_process_effect_instance on process_effect (tenant_id, instance_id);

create table process_event (
    tenant_id text not null,
    event_id text not null,
    name text not null,
    received_at timestamptz not null,
    constraint pk_process_event primary key (tenant_id, event_id)
);

create table process_inbox (
    tenant_id text not null,
    inbox_id bigint generated always as identity,
    instance_id text not null,
    kind smallint not null,
    payload text not null,
    created_at timestamptz not null,
    status smallint not null,
    code text null,
    constraint pk_process_inbox primary key (tenant_id, inbox_id)
);

create index ix_process_inbox_status on process_inbox (status, inbox_id);
create index ix_process_inbox_instance_pending on process_inbox (tenant_id, instance_id, inbox_id) where status = 1;
