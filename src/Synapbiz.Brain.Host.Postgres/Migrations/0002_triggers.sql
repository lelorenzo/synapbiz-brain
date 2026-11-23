-- The tables of the triggers (W1-C): activations, event triggers and schedules. Applied once, by the migrator, with the search path
-- set to the configured schema. Every table has the tenant as the first column of its primary key. Times are timestamptz and come
-- from the application clock.

-- One activation per tenant and process id: the pinned definition (version and hash), the zone the consumer supplied and the
-- protected input object that every instance started by a trigger of the process begins with.
create table process_activation (
    tenant_id text not null,
    process_id text not null,
    process_version bigint not null,
    process_hash text not null,
    zone text null,
    input text not null,
    activated_at timestamptz not null,
    constraint pk_process_activation primary key (tenant_id, process_id)
);

-- One row per event trigger of an activated process. The business key member and the mapping of payload members to input variables
-- are copied from the pinned definition, so that publishing an event never depends on the catalog; they are names, never values.
create table process_event_trigger (
    tenant_id text not null,
    event text not null,
    process_id text not null,
    business_key_member text not null,
    input_map text not null,
    constraint pk_process_event_trigger primary key (tenant_id, event, process_id)
);

create index ix_process_event_trigger_process on process_event_trigger (tenant_id, process_id);

-- One row per schedule trigger of an activated process. The zone is the zone the schedule is evaluated in (the zone of the trigger,
-- else the one of the activation). next_fire_at is the first occurrence that has not been handled yet.
create table process_schedule (
    tenant_id text not null,
    process_id text not null,
    schedule_id text not null,
    cron text not null,
    zone text not null,
    misfire smallint not null,
    next_fire_at timestamptz not null,
    constraint pk_process_schedule primary key (tenant_id, process_id, schedule_id)
);

create index ix_process_schedule_due on process_schedule (next_fire_at, tenant_id, process_id, schedule_id);
